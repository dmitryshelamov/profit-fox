using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Backtests.Commands.RunNextBacktestWork;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Queries;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>A strategy, a bot on <c>linear</c> / <c>BTCUSDT</c> at one hour and its backtests, in a running app.</summary>
public sealed class BacktestScenario(ProfitFoxFactory factory, HttpClient client)
{
    public static readonly Instrument BtcLinear = new Instrument(MarketCategory.Linear, "BTCUSDT");
    public static readonly InstrumentRules Rules = new InstrumentRules(0.01m, 0.001m, 0.001m, 1m);

    public Guid StrategyId { get; private set; }

    public Guid BotId { get; private set; }

    public async Task CreateBotAsync(string strategy, object? parameterValues = null)
    {
        factory.Bybit.Instruments.Add(("linear", "BTCUSDT"));
        HttpResponseMessage created = await client.PostAsJsonAsync("/api/strategies", new { name = strategy, definition = StrategyFixtures.Element(strategy) });
        StrategyId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        HttpResponseMessage bot = await client.PostAsJsonAsync(
            "/api/bots",
            new { name = $"{strategy} BTC", strategyId = StrategyId, parameterValues = parameterValues ?? new { }, category = "linear", symbol = "BTCUSDT", interval = "60", balance = 1000 });
        BotId = (await bot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Hourly candles from <paramref name="from" />, one per close price, with the high and low 1 % off.</summary>
    public async Task StoreCandlesAsync(DateTimeOffset from, params decimal[] closes)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICandleRepository>().UpsertAsync(BtcLinear, Interval.OneHour, Candles(from, closes), CancellationToken.None);
    }

    public static IReadOnlyList<Candle> Candles(DateTimeOffset from, params decimal[] closes)
    {
        return
        [
            .. closes.Select((close, hour) => new Candle(
                from.AddHours(hour),
                new Price(hour == 0 ? close : closes[hour - 1]),
                new Price(close * 1.01m),
                new Price(close * 0.99m),
                new Price(close),
                new Volume(1m),
                new Volume(close)))
        ];
    }

    /// <summary>
    ///     Saves a pending backtest of the bot, as creating it through the API does, and a running Worker takes it; a backtest saved finished
    ///     stays as it is.
    /// </summary>
    public async Task<Backtest> CreateAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyDictionary<string, ParameterSpec>? parameters = null,
        BacktestStatus status = BacktestStatus.Pending,
        int engineVersion = EngineVersion.Current)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IStrategyLibraryRepository library = scope.ServiceProvider.GetRequiredService<IStrategyLibraryRepository>();
        Bot bot = (await library.GetBotAsync(BotId, CancellationToken.None))!;
        StrategyVersion version = (await library.GetVersionAsync(StrategyId, (await library.GetLatestVersionNumberAsync(StrategyId, CancellationToken.None))!.Value, CancellationToken.None))!;
        var policy = new BacktestPolicy(10000, new Dictionary<MarketCategory, FeeRates> { [MarketCategory.Linear] = new FeeRates(0.02m, 0.055m) });
        Backtest backtest = Backtest.Create(
            Guid.NewGuid(),
            bot,
            version,
            from,
            to,
            parameters ?? new Dictionary<string, ParameterSpec>(),
            new SimulationRequest(null, null, null, null, null),
            policy,
            Rules,
            engineVersion,
            factory.Time.GetUtcNow()) with
        { Status = status };
        await scope.ServiceProvider.GetRequiredService<IBacktestRepository>().AddAsync(backtest, CancellationToken.None);

        return backtest;
    }

    /// <summary>Stores results of the backtest as a Worker would, outside any chunk.</summary>
    public async Task StoreResultsAsync(params BacktestResult[] results)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await using NpgsqlConnection connection = await scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        foreach (BacktestResult result in results)
        {
            await connection.ExecuteAsync(BacktestRepositoryQueries.InsertResult(BacktestResultDbConverter.FromDomain(result), transaction, CancellationToken.None));
        }

        await transaction.CommitAsync();
    }

    public async Task<Backtest> GetAsync(Guid id)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();

        return (await scope.ServiceProvider.GetRequiredService<IBacktestRepository>().GetAsync(id, CancellationToken.None))!;
    }

    public async Task<IReadOnlyList<BacktestResult>> ResultsAsync(Backtest backtest)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IBacktestRepository repository = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();
        var results = new List<BacktestResult>();

        foreach (long combination in await repository.GetCalculatedCombinationsAsync(backtest.Id, CancellationToken.None))
        {
            results.Add((await repository.GetResultAsync(backtest.Id, combination, CancellationToken.None))!);
        }

        return results;
    }

    /// <summary>Waits for the status, moving the app's clock a second at a time so that the waits of the backtest pass.</summary>
    public async Task<Backtest> WaitForAsync(Guid id, params BacktestStatus[] statuses)
    {
        var waiting = Stopwatch.StartNew();

        while (true)
        {
            Backtest backtest = await GetAsync(id);

            if (statuses.Contains(backtest.Status) || waiting.Elapsed > TimeSpan.FromSeconds(30))
            {
                return backtest;
            }

            factory.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }
    }

    public Task<Backtest> WaitUntilFinishedAsync(Guid id)
    {
        return WaitForAsync(id, BacktestStatus.Succeeded, BacktestStatus.Failed, BacktestStatus.Cancelled);
    }

    /// <summary>A Worker's handler with one thread over the services of the scope and the given repository and clock, a Worker of its own.</summary>
    public static RunNextBacktestWorkCommandHandler Handler(
        AsyncServiceScope scope,
        IBacktestRepository backtests,
        TimeProvider time,
        WorkerSettings? settings = null,
        EngineVersion? engine = null)
    {
        return new RunNextBacktestWorkCommandHandler(
            backtests,
            scope.ServiceProvider.GetRequiredService<IStrategyLibraryRepository>(),
            scope.ServiceProvider.GetRequiredService<ICandleRepository>(),
            scope.ServiceProvider.GetRequiredService<ICandleImportJobRepository>(),
            new BacktestInputCache(),
            new BacktestSettings { MaxParallelism = 1 },
            settings ?? new WorkerSettings(),
            new WorkerIdentity(Guid.NewGuid()),
            engine ?? new EngineVersion(EngineVersion.Current),
            time);
    }

    /// <summary>Lets the handler take work until it finds none, as a Worker that nobody stops.</summary>
    public static async Task RunUntilIdleAsync(RunNextBacktestWorkCommandHandler handler)
    {
        while (await handler.Handle(new RunNextBacktestWorkCommand(), CancellationToken.None))
        {
        }
    }

    /// <summary>
    ///     Passes every call to the stored repository and, after each save of chunk progress, tells the backtest as stored then (<c>null</c>
    ///     once deleted) and whether the save was written.
    /// </summary>
    public static IBacktestRepository Spy(IBacktestRepository stored, Action<Backtest?, bool> saved)
    {
        IBacktestRepository spy = Substitute.For<IBacktestRepository>();
        spy.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => stored.GetAsync(call.Arg<Guid>(), call.Arg<CancellationToken>()));
        spy.GetUnfinishedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call => stored.GetUnfinishedAsync(call.Arg<int>(), call.Arg<CancellationToken>()));
        spy.FailOutdatedAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => stored.FailOutdatedAsync(call.Arg<int>(), call.Arg<string>()!, call.Arg<DateTimeOffset>(), call.Arg<CancellationToken>()));
        spy.UpdateAsync(Arg.Any<Backtest>(), Arg.Any<BacktestStatus>(), Arg.Any<CancellationToken>())
            .Returns(call => stored.UpdateAsync(call.Arg<Backtest>()!, call.Arg<BacktestStatus>(), call.Arg<CancellationToken>()));
        spy.FailAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<BacktestStatus>>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => stored.FailAsync(call.ArgAt<Guid>(0), call.ArgAt<IReadOnlyCollection<BacktestStatus>>(1)!, call.ArgAt<string>(2)!, call.ArgAt<DateTimeOffset>(3), call.ArgAt<CancellationToken>(4)));
        spy.StartImportAsync(Arg.Any<Backtest>(), Arg.Any<CandleImportJob>(), Arg.Any<CancellationToken>())
            .Returns(call => stored.StartImportAsync(call.Arg<Backtest>()!, call.Arg<CandleImportJob>()!, call.Arg<CancellationToken>()));
        spy.StartRunningAsync(Arg.Any<Backtest>(), Arg.Any<IReadOnlyList<BacktestChunk>>(), Arg.Any<CancellationToken>())
            .Returns(call => stored.StartRunningAsync(call.Arg<Backtest>()!, call.Arg<IReadOnlyList<BacktestChunk>>()!, call.Arg<CancellationToken>()));
        spy.ClaimChunkAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => stored.ClaimChunkAsync(call.ArgAt<Guid>(0), call.ArgAt<Guid>(1), call.ArgAt<DateTimeOffset>(2), call.ArgAt<DateTimeOffset>(3), call.ArgAt<CancellationToken>(4)));
        spy.RenewChunkLeaseAsync(Arg.Any<BacktestChunk>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => stored.RenewChunkLeaseAsync(call.Arg<BacktestChunk>()!, call.Arg<Guid>(), call.Arg<DateTimeOffset>(), call.Arg<CancellationToken>()));
        spy.GetCalculatedCombinationsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => stored.GetCalculatedCombinationsAsync(call.Arg<Guid>(), call.Arg<CancellationToken>()));
        spy.GetCalculatedCombinationsAsync(Arg.Any<BacktestChunk>(), Arg.Any<CancellationToken>())
            .Returns(call => stored.GetCalculatedCombinationsAsync(call.Arg<BacktestChunk>()!, call.Arg<CancellationToken>()));
        spy.CompleteChunkAsync(Arg.Any<BacktestChunk>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => stored.CompleteChunkAsync(call.Arg<BacktestChunk>()!, call.Arg<Guid>(), call.Arg<CancellationToken>()));
        spy.SucceedIfCompleteAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => stored.SucceedIfCompleteAsync(call.Arg<Guid>(), call.Arg<DateTimeOffset>(), call.Arg<CancellationToken>()));
        spy.SaveChunkProgressAsync(
                Arg.Any<BacktestChunk>(),
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<BacktestResult>>(),
                Arg.Any<decimal?>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                BacktestChunk chunk = call.ArgAt<BacktestChunk>(0)!;
                bool written = await stored.SaveChunkProgressAsync(
                    chunk,
                    call.ArgAt<Guid>(1),
                    call.ArgAt<IReadOnlyList<BacktestResult>>(2)!,
                    call.ArgAt<decimal?>(3),
                    call.ArgAt<DateTimeOffset>(4),
                    call.ArgAt<CancellationToken>(5));
                saved(await stored.GetAsync(chunk.BacktestId, CancellationToken.None), written);

                return written;
            });

        return spy;
    }
}