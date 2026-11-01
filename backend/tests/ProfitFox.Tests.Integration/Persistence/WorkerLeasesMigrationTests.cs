using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Queries;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Work a deploy leaves running, from before the Workers, goes on in the first Worker after the migration to leases and chunks.</summary>
public class WorkerLeasesMigrationTests
{
    // The last migration before leases and chunks.
    private const long BeforeLeases = 202611020003;

    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private string _connectionString = null!;
    private ServiceProvider _migrations = null!;
    private NpgsqlDataSource _dataSource = null!;

    [SetUp]
    public async Task SetUp()
    {
        _connectionString = await IntegrationEnvironment.CreateDatabaseAsync();
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MarketData"] = _connectionString })
            .Build();
        var services = new ServiceCollection();
        PostgresRegistrar.Configure(configuration, services);
        _migrations = services.BuildServiceProvider();
        Migrate(BeforeLeases);
        _dataSource = NpgsqlDataSource.Create(_connectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
        await _migrations.DisposeAsync();
    }

    [Test]
    public async Task Running_job_keeps_its_stalled_restarts_as_lost_leases_and_is_free_for_the_first_worker()
    {
        CandleImportJob job = CandleImportJobGenerator.Generate(CandleImportStatus.Running) with { StartedAt = Created, ImportedCandles = 4, LostLeases = 2 };
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO candle_import_jobs
                    (id, category, symbol, "interval", "from", "to", status, imported_candles, error, created_at, started_at, finished_at, stalled_restarts)
                VALUES
                    (@Id, @Category, @Symbol, @Interval, @From, @To, @Status, @ImportedCandles, @Error, @CreatedAt, @StartedAt, @FinishedAt, @LostLeases)
                """,
                CandleImportJobDbConverter.FromDomain(job));
        }

        Migrate(null);

        var jobs = new CandleImportJobRepository(_dataSource);
        (await jobs.GetAsync(job.Id, CancellationToken.None)).ShouldBe(job);
        (await jobs.ClaimNextAsync(Guid.NewGuid(), Created, Created.AddSeconds(30), CancellationToken.None)).ShouldBe(job);
    }

    [Test]
    public async Task Running_backtest_goes_back_to_loading_with_its_results_so_that_a_worker_splits_it()
    {
        Bot bot = await AddBotAsync();
        Backtest running = Backtest(bot) with { Status = BacktestStatus.Running, StartedAt = Created, CalculatedCombinations = 1 };
        var result = new BacktestResult(
            running.Id,
            0,
            new Dictionary<string, NodeValue>(),
            new BacktestMetrics(1000m, 1000m, 0m, 0m, 0m, 0m, 0m, null, 0, null, 0, 0m, 0m, 0m, 0m, 0m, 0m, 0, 0, 744),
            TimeSpan.FromMilliseconds(1));
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            var row = new DynamicParameters(BacktestDbConverter.FromDomain(running));
            row.Add("StalledRestarts", 1);
            await connection.ExecuteAsync(
                """
                INSERT INTO backtests (id, bot_id, strategy_id, strategy_version, category, symbol, "interval", "from", "to", parameters, balance, limit_fee_percent, market_fee_percent, slippage_percent, limit_fill_mode, tick_size, quantity_step, min_quantity, min_amount, search_mode, status, error, import_job_id, actual_start, gaps, loaded_candles, expected_candles, calculated_combinations, total_combinations, speed, stalled_restarts, created_at, started_at, finished_at)
                VALUES (@Id, @BotId, @StrategyId, @StrategyVersion, @Category, @Symbol, @Interval, @From, @To, @Parameters::jsonb, @Balance, @LimitFeePercent, @MarketFeePercent, @SlippagePercent, @LimitFillMode, @TickSize, @QuantityStep, @MinQuantity, @MinAmount, @SearchMode, @Status, @Error, @ImportJobId, @ActualStart, @Gaps::jsonb, @LoadedCandles, @ExpectedCandles, @CalculatedCombinations, @TotalCombinations, @Speed, @StalledRestarts, @CreatedAt, @StartedAt, @FinishedAt)
                """,
                row);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            await connection.ExecuteAsync(BacktestRepositoryQueries.InsertResult(BacktestResultDbConverter.FromDomain(result), transaction, CancellationToken.None));
            await transaction.CommitAsync();
        }

        Migrate(null);

        var backtests = new BacktestRepository(_dataSource);
        Backtest loading = (await backtests.GetAsync(running.Id, CancellationToken.None))!;
        (loading.Status, loading.CalculatedCombinations).ShouldBe((BacktestStatus.LoadingCandles, 1L));
        (await backtests.GetCalculatedCombinationsAsync(running.Id, CancellationToken.None)).ShouldBe([0L]);
    }

    /// <summary>Migrates up to <paramref name="version" />, or to the latest one.</summary>
    private void Migrate(long? version)
    {
        using IServiceScope scope = _migrations.CreateScope();
        IMigrationRunner runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();

        if (version is { } target)
        {
            runner.MigrateUp(target);
        }
        else
        {
            runner.MigrateUp();
        }
    }

    private async Task<Bot> AddBotAsync()
    {
        var library = new StrategyLibrary(0, [], []);
        Strategy ema = library.AddStrategy(Guid.NewGuid(), "EMA", StrategyJson.ParseStrategy(StrategyFixtures.Element("ema")), Created);
        Bot bot = library.AddBot(Guid.NewGuid(), "EMA BTC", ema.Id, new Dictionary<string, NodeValue>(), new Instrument(MarketCategory.Linear, "BTCUSDT"), Interval.OneHour, 1000m, Created);
        await new StrategyLibraryRepository(_dataSource).SaveAsync(library.Change, 0, CancellationToken.None);

        return bot;
    }

    private static Backtest Backtest(Bot bot)
    {
        return new Backtest(
            Guid.NewGuid(),
            bot.Id,
            bot.StrategyId,
            1,
            bot.Instrument,
            bot.Interval,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2024-02-01T00:00:00Z"),
            new Dictionary<string, ParameterSpec> { ["fast"] = ParameterSpec.Of(new NumberValue(12m)) },
            new SimulationSettings(1000m, 0.02m, 0.055m, 0m, LimitFillMode.Breakout),
            new InstrumentRules(0.1m, 0.001m, 0.001m, 5m),
            SearchMode.Grid,
            BacktestStatus.Pending,
            null,
            null,
            null,
            [],
            0,
            0,
            0,
            1,
            null,
            Created,
            null,
            null,
            EngineVersion.Current);
    }
}