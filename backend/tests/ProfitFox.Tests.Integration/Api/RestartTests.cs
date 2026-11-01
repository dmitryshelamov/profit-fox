using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Npgsql;

using ProfitFox.Application.Backtests.Commands.RunBacktest;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class RestartTests
{
    private static readonly Instrument BtcLinear = new Instrument(MarketCategory.Linear, "BTCUSDT");

    // Scenario "Перезапуск во время загрузки": the job had stored the 11:00 candle when the service stopped.
    [Test]
    public async Task Job_running_when_the_service_stopped_loads_only_the_missing_candles_after_start()
    {
        string connectionString = await IntegrationEnvironment.CreateDatabaseAsync();
        CandleImportJob running = await StartAndLeaveRunningJobAsync(connectionString);

        await using var restarted = new ProfitFoxFactory(connectionString);
        restarted.Bybit.Klines.AddRange(
        [
            ["2024-03-10T11:00:00Z", "3", "3", "3", "3", "1", "3"],
            ["2024-03-10T10:00:00Z", "2", "2", "2", "2", "1", "2"],
            ["2024-03-10T09:00:00Z", "1", "1", "1", "1", "1", "1"]
        ]);
        restarted.CreateClient().Dispose();

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        CandleImportJob job = await WaitUntilFinishedAsync(new CandleImportJobRepository(dataSource), running.Id);
        job.Status.ShouldBe(CandleImportStatus.Succeeded);
        job.ImportedCandles.ShouldBe(running.ImportedCandles + 2);
        restarted.Bybit.KlineRequests.ShouldBe([(At("2024-03-10T09:00:00Z"), At("2024-03-10T10:59:59.999Z"))]);
        IReadOnlyList<DateTimeOffset> stored = await new CandleRepository(dataSource)
            .GetOpenTimesAsync(BtcLinear, Interval.OneHour, running.From, running.To, CancellationToken.None);
        stored.ShouldBe([At("2024-03-10T09:00:00Z"), At("2024-03-10T10:00:00Z"), At("2024-03-10T11:00:00Z")]);
    }

    // Scenario "Перезапуск во время прогона".
    [Test]
    public async Task Backtest_stopped_after_five_combinations_calculates_the_other_seven_after_start()
    {
        string connectionString = await IntegrationEnvironment.CreateDatabaseAsync();
        DateTimeOffset from = At("2024-03-01T00:00:00Z");
        decimal[] closes = [100m, 98m, 97m, 99m, 101m, 100m, 98m, 96m, 97m, 99m, 102m, 103m];
        var grid = new Dictionary<string, ParameterSpec>
        {
            ["priceDropForBuy"] = ParameterSpec.Between(new NumberValue(0.01m), new NumberValue(0.03m), new NumberValue(0.01m)),
            ["priceRiseForSell"] = ParameterSpec.Between(new NumberValue(0.005m), new NumberValue(0.02m), new NumberValue(0.005m))
        };
        Guid interrupted;
        Guid whole;
        IReadOnlyList<BacktestResult> expected;

        await using (var first = new ProfitFoxFactory(connectionString))
        {
            using HttpClient client = first.CreateClient();
            var scenario = new BacktestScenario(first, client);
            await scenario.CreateBotAsync("ladder");
            await scenario.StoreCandlesAsync(from, closes);
            Backtest reference = await scenario.QueueAsync(from, from.AddHours(closes.Length), grid);
            expected = await scenario.ResultsAsync(await scenario.WaitUntilFinishedAsync(reference.Id));
            whole = reference.Id;
            Backtest backtest = await scenario.CreateAsync(from, from.AddHours(closes.Length), grid);
            interrupted = backtest.Id;
            await using AsyncServiceScope scope = first.Services.CreateAsyncScope();
            IBacktestRepository spy = BacktestScenario.Spy(
                scope.ServiceProvider.GetRequiredService<IBacktestRepository>(),
                (saved, _) =>
                {
                    if (saved.CalculatedCombinations == 5)
                    {
                        throw new OperationCanceledException("The service stops.");
                    }
                });
            var ticking = new FakeTimeProvider(first.Time.GetUtcNow()) { AutoAdvanceAmount = TimeSpan.FromSeconds(1) };

            await Should.ThrowAsync<OperationCanceledException>(() =>
                BacktestScenario.Handler(scope, spy, ticking).Handle(new RunBacktestCommand(backtest.Id), CancellationToken.None));
        }

        await using var restarted = new ProfitFoxFactory(connectionString);
        using HttpClient restartedClient = restarted.CreateClient();
        var after = new BacktestScenario(restarted, restartedClient);

        Backtest resumed = await after.WaitUntilFinishedAsync(interrupted);

        resumed.Status.ShouldBe(BacktestStatus.Succeeded);
        resumed.CalculatedCombinations.ShouldBe(12L);
        IReadOnlyList<BacktestResult> results = await after.ResultsAsync(resumed);
        results.Select(result => (result.Combination, result.Metrics)).ShouldBe(expected.Select(result => (result.Combination, result.Metrics)));
        whole.ShouldNotBe(interrupted);
    }

    // Scenario "Бэктест без продвижения".
    [Test]
    public async Task Backtest_interrupted_for_the_third_time_without_a_new_result_fails_after_start()
    {
        string connectionString = await IntegrationEnvironment.CreateDatabaseAsync();
        Guid stalled;

        await using (var first = new ProfitFoxFactory(connectionString))
        {
            using HttpClient client = first.CreateClient();
            var scenario = new BacktestScenario(first, client);
            await scenario.CreateBotAsync("ladder");
            Backtest backtest = await scenario.CreateAsync(At("2024-03-01T00:00:00Z"), At("2024-03-01T12:00:00Z"));
            await using AsyncServiceScope scope = first.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IBacktestRepository>()
                .UpdateAsync(backtest with { Status = BacktestStatus.Running, StalledRestarts = 2 }, BacktestStatus.Pending, CancellationToken.None);
            stalled = backtest.Id;
        }

        await using var restarted = new ProfitFoxFactory(connectionString);
        using HttpClient restartedClient = restarted.CreateClient();

        Backtest failed = await new BacktestScenario(restarted, restartedClient).GetAsync(stalled);

        (failed.Status, failed.Error, failed.StalledRestarts).ShouldBe((BacktestStatus.Failed, Backtest.InterruptedByRestart, 3));
    }

    private static async Task<CandleImportJob> StartAndLeaveRunningJobAsync(string connectionString)
    {
        await using var factory = new ProfitFoxFactory(connectionString);
        factory.CreateClient().Dispose();
        CandleImportJob job = CandleImportJobGenerator.Generate(CandleImportStatus.Running) with
        {
            Instrument = BtcLinear,
            Interval = Interval.OneHour,
            From = At("2024-03-10T09:00:00Z"),
            To = At("2024-03-10T12:00:00Z"),
            ImportedCandles = 1,
            StartedAt = factory.Time.GetUtcNow()
        };
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await new CandleImportJobRepository(dataSource).AddAsync(job, CancellationToken.None);
        await new CandleRepository(dataSource).UpsertAsync(BtcLinear, Interval.OneHour, [CandleGenerator.Generate(At("2024-03-10T11:00:00Z"))], CancellationToken.None);

        return job;
    }

    private static async Task<CandleImportJob> WaitUntilFinishedAsync(CandleImportJobRepository jobs, Guid id)
    {
        var waiting = Stopwatch.StartNew();

        while (true)
        {
            CandleImportJob job = (await jobs.GetAsync(id, CancellationToken.None))!;

            if (job.Status is CandleImportStatus.Succeeded or CandleImportStatus.Failed || waiting.Elapsed > TimeSpan.FromSeconds(30))
            {
                return job;
            }

            await Task.Delay(50);
        }
    }

    private static DateTimeOffset At(string time)
    {
        return DateTimeOffset.Parse(time);
    }
}