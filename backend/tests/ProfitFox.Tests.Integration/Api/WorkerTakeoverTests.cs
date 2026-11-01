using System.Collections.Concurrent;
using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Npgsql;

using NSubstitute;

using ProfitFox.Application.Backtests.Commands.RunNextBacktestWork;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirements "Перехват истёкшей аренды", "Продолжение после сбоя Worker'а" and "Прерванные задачи после сбоя Worker'а".</summary>
public class WorkerTakeoverTests
{
    private static readonly Instrument BtcLinear = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-03-01T00:00:00Z");
    private static readonly decimal[] Closes = [100m, 98m, 97m, 99m, 101m, 100m, 98m, 96m, 97m, 99m, 102m, 103m];

    // Scenario "Worker упал во время загрузки": a stopped Worker had stored the 11:00 candle and still holds the lease.
    [Test]
    public async Task Job_of_a_stopped_worker_is_taken_over_after_its_lease_and_loads_only_the_missing_candles()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync(workers: 0);
        factory.CreateClient().Dispose();
        await using var dataSource = NpgsqlDataSource.Create(factory.ConnectionString);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ICandleImportJobRepository jobs = scope.ServiceProvider.GetRequiredService<ICandleImportJobRepository>();
        CandleImportJob job = CandleImportJobGenerator.Generate(CandleImportStatus.Pending) with
        {
            Instrument = BtcLinear,
            Interval = Interval.OneHour,
            From = At("2024-03-10T09:00:00Z"),
            To = At("2024-03-10T12:00:00Z")
        };
        await jobs.AddAsync(job, CancellationToken.None);
        DateTimeOffset now = factory.Time.GetUtcNow();
        var stopped = Guid.NewGuid();
        CandleImportJob running = (await jobs.ClaimNextAsync(stopped, now, now.AddSeconds(30), CancellationToken.None))!.TakenOver(now) with { ImportedCandles = 1 };
        await jobs.UpdateLeasedAsync(running, stopped, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ICandleRepository>()
            .UpsertAsync(BtcLinear, Interval.OneHour, [CandleGenerator.Generate(At("2024-03-10T11:00:00Z"))], CancellationToken.None);
        factory.Bybit.Klines.AddRange(
        [
            ["2024-03-10T11:00:00Z", "3", "3", "3", "3", "1", "3"],
            ["2024-03-10T10:00:00Z", "2", "2", "2", "2", "1", "2"],
            ["2024-03-10T09:00:00Z", "1", "1", "1", "1", "1", "1"]
        ]);
        await using WorkerHost worker = factory.StartWorker();

        await Task.Delay(200);
        factory.Bybit.KlineRequests.ShouldBeEmpty();
        CandleImportJob finished = await WaitUntilFinishedAsync(factory, jobs, job.Id);

        (finished.Status, finished.ImportedCandles, finished.LostLeases).ShouldBe((CandleImportStatus.Succeeded, 3L, 0));
        factory.Bybit.KlineRequests.ShouldBe([(At("2024-03-10T09:00:00Z"), At("2024-03-10T10:59:59.999Z"))]);
    }

    // Scenario "Задача без продвижения": a running job with no lease, as a deploy leaves it, is free at once.
    [Test]
    public async Task Job_taken_over_for_the_third_time_without_progress_fails()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync();
        factory.CreateClient().Dispose();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ICandleImportJobRepository jobs = scope.ServiceProvider.GetRequiredService<ICandleImportJobRepository>();
        CandleImportJob job = CandleImportJobGenerator.Generate(CandleImportStatus.Running) with { StartedAt = factory.Time.GetUtcNow(), LostLeases = 2 };
        await jobs.AddAsync(job, CancellationToken.None);

        CandleImportJob failed = await WaitUntilFinishedAsync(factory, jobs, job.Id);

        (failed.Status, failed.Error, failed.LostLeases).ShouldBe((CandleImportStatus.Failed, CandleImportJob.InterruptedWithoutProgress, 3));
        factory.Bybit.KlineRequests.ShouldBeEmpty();
    }

    // Scenarios "Worker упал во время прогона", "Перезапуск всех Worker'ов" and "Worker остановлен посреди куска".
    [Test]
    public async Task Backtest_of_a_worker_that_stopped_after_five_combinations_is_finished_by_another_with_the_same_results()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync(workers: 0);
        using HttpClient client = factory.CreateClient();
        var scenario = new BacktestScenario(factory, client);
        await scenario.CreateBotAsync("ladder");
        await scenario.StoreCandlesAsync(From, Closes);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IBacktestRepository stored = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();
        Backtest reference = await scenario.CreateAsync(From, From.AddHours(Closes.Length), Grid());
        await BacktestScenario.RunUntilIdleAsync(BacktestScenario.Handler(scope, stored, Ticking(factory)));
        IReadOnlyList<BacktestResult> expected = await scenario.ResultsAsync(await scenario.GetAsync(reference.Id));
        Backtest backtest = await scenario.CreateAsync(From, From.AddHours(Closes.Length), Grid());
        IBacktestRepository spy = BacktestScenario.Spy(stored, (saved, _) =>
        {
            if (saved?.CalculatedCombinations == 5)
            {
                throw new OperationCanceledException("The Worker stops.");
            }
        });
        await Should.ThrowAsync<OperationCanceledException>(() =>
            BacktestScenario.Handler(scope, spy, Ticking(factory)).Handle(new RunNextBacktestWorkCommand(), CancellationToken.None));

        await using WorkerHost worker = factory.StartWorker();
        Backtest resumed = await scenario.WaitUntilFinishedAsync(backtest.Id);

        (resumed.Status, resumed.CalculatedCombinations).ShouldBe((BacktestStatus.Succeeded, 12L));
        IReadOnlyList<BacktestResult> results = await scenario.ResultsAsync(resumed);
        results.Select(result => (result.Combination, result.Metrics)).ShouldBe(expected.Select(result => (result.Combination, result.Metrics)));
    }

    // Scenario "Кусок без продвижения".
    [Test]
    public async Task Chunk_whose_lease_expired_for_the_third_time_without_a_result_fails_the_backtest()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync(workers: 0);
        using HttpClient client = factory.CreateClient();
        var scenario = new BacktestScenario(factory, client);
        await scenario.CreateBotAsync("ladder");
        await scenario.StoreCandlesAsync(From, Closes);
        Backtest backtest = await scenario.CreateAsync(From, From.AddHours(Closes.Length));
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IBacktestRepository stored = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();
        DateTimeOffset now = factory.Time.GetUtcNow();
        Backtest loading = backtest.StartLoading(now);
        await stored.UpdateAsync(loading, BacktestStatus.Pending, CancellationToken.None);
        await stored.StartRunningAsync(loading.StartRunning(From, []), BacktestChunk.Split(backtest.Id, 1, 100), CancellationToken.None);

        foreach (int lease in Enumerable.Range(0, 3))
        {
            await stored.ClaimChunkAsync(backtest.Id, Guid.NewGuid(), now.AddSeconds(30 * lease), now.AddSeconds(30 * (lease + 1)), CancellationToken.None);
        }

        await using WorkerHost worker = factory.StartWorker();
        Backtest failed = await scenario.WaitUntilFinishedAsync(backtest.Id);

        (failed.Status, failed.Error).ShouldBe((BacktestStatus.Failed, Backtest.InterruptedChunk));
        (await stored.GetCalculatedCombinationsAsync(backtest.Id, CancellationToken.None)).ShouldBeEmpty();
    }

    // Scenarios "Два Worker'а на одной базе" and "Тот же результат на разном числе Worker'ов".
    [Test]
    public async Task Two_workers_share_the_chunks_of_one_grid_and_give_the_results_of_one_worker()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync(workers: 0);
        using HttpClient client = factory.CreateClient();
        var scenario = new BacktestScenario(factory, client);
        await scenario.CreateBotAsync("ladder");
        await scenario.StoreCandlesAsync(From, Closes);
        var chunks = new Dictionary<string, string?> { ["Worker:ChunkSize"] = "4" };
        Backtest single = await scenario.CreateAsync(From, From.AddHours(Closes.Length), Grid());
        IReadOnlyList<BacktestResult> one;

        await using (WorkerHost alone = factory.StartWorker(workerSettings: chunks))
        {
            one = await scenario.ResultsAsync(await scenario.WaitUntilFinishedAsync(single.Id));
        }

        var owners = new ConcurrentDictionary<Guid, int>();
        var bothClaimed = new TaskCompletionSource();
        Backtest shared = await scenario.CreateAsync(From, From.AddHours(Closes.Length), Grid());
        await using WorkerHost first = factory.StartWorker(services => WaitForBothClaims(services, owners, bothClaimed), workerSettings: chunks);
        await using WorkerHost second = factory.StartWorker(services => WaitForBothClaims(services, owners, bothClaimed), workerSettings: chunks);

        Backtest finished = await scenario.WaitUntilFinishedAsync(shared.Id);

        (finished.Status, finished.CalculatedCombinations).ShouldBe((BacktestStatus.Succeeded, 12L));
        owners.Count.ShouldBe(2);
        IReadOnlyList<BacktestResult> two = await scenario.ResultsAsync(finished);
        two.Select(result => (result.Combination, result.Metrics)).ShouldBe(one.Select(result => (result.Combination, result.Metrics)));
    }

    // Scenario "Очередь": one Worker finishes the chunks of the older backtest before it starts the newer one.
    [Test]
    public async Task One_worker_finishes_the_older_backtest_before_it_starts_the_newer()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync(workers: 0);
        using HttpClient client = factory.CreateClient();
        var scenario = new BacktestScenario(factory, client);
        await scenario.CreateBotAsync("ladder");
        await scenario.StoreCandlesAsync(From, Closes);
        Backtest older = await scenario.CreateAsync(From, From.AddHours(Closes.Length), Grid());
        factory.Time.Advance(TimeSpan.FromSeconds(1));
        Backtest newer = await scenario.CreateAsync(From, From.AddHours(Closes.Length), Grid());
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IBacktestRepository stored = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();
        IBacktestRepository spy = BacktestScenario.Spy(stored, (_, _) => { });

        await BacktestScenario.RunUntilIdleAsync(BacktestScenario.Handler(scope, spy, Ticking(factory), new() { ChunkSize = 4 }));

        spy.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IBacktestRepository.CompleteChunkAsync))
            .Select(call => ((BacktestChunk)call.GetArguments()[0]!).BacktestId)
            .ShouldBe([older.Id, older.Id, older.Id, newer.Id, newer.Id, newer.Id]);
    }

    // Scenario "Свободный Worker начинает следующий бэктест".
    [Test]
    public async Task Worker_that_finds_every_chunk_of_the_older_backtest_taken_starts_the_newer()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync(workers: 0);
        using HttpClient client = factory.CreateClient();
        var scenario = new BacktestScenario(factory, client);
        await scenario.CreateBotAsync("ladder");
        await scenario.StoreCandlesAsync(From, Closes);
        Backtest older = await scenario.CreateAsync(From, From.AddHours(Closes.Length), Grid());
        factory.Time.Advance(TimeSpan.FromSeconds(1));
        Backtest newer = await scenario.CreateAsync(From, From.AddHours(Closes.Length));
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IBacktestRepository stored = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();
        DateTimeOffset now = factory.Time.GetUtcNow();
        Backtest loading = older.StartLoading(now);
        await stored.UpdateAsync(loading, BacktestStatus.Pending, CancellationToken.None);
        await stored.StartRunningAsync(loading.StartRunning(From, []), BacktestChunk.Split(older.Id, older.TotalCombinations, 100), CancellationToken.None);
        await stored.ClaimChunkAsync(older.Id, Guid.NewGuid(), now, now.AddMinutes(10), CancellationToken.None);

        bool found = await BacktestScenario.Handler(scope, stored, factory.Time).Handle(new RunNextBacktestWorkCommand(), CancellationToken.None);

        found.ShouldBeTrue();
        (await scenario.GetAsync(newer.Id)).Status.ShouldBe(BacktestStatus.Succeeded);
        (await scenario.GetAsync(older.Id)).CalculatedCombinations.ShouldBe(0L);
    }

    /// <summary>Holds each Worker after its first claimed chunk until the other Worker has claimed one too, so that both take part.</summary>
    private static void WaitForBothClaims(IServiceCollection services, ConcurrentDictionary<Guid, int> owners, TaskCompletionSource bothClaimed)
    {
        services.AddScoped(provider =>
        {
            IBacktestRepository spy = BacktestScenario.Spy(new BacktestRepository(provider.GetRequiredService<NpgsqlDataSource>()), (_, _) => { });
            var stored = new BacktestRepository(provider.GetRequiredService<NpgsqlDataSource>());
            spy.ClaimChunkAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    BacktestChunk? chunk = await stored.ClaimChunkAsync(
                        call.ArgAt<Guid>(0),
                        call.ArgAt<Guid>(1),
                        call.ArgAt<DateTimeOffset>(2),
                        call.ArgAt<DateTimeOffset>(3),
                        call.ArgAt<CancellationToken>(4));

                    if (chunk is not null && owners.TryAdd(call.ArgAt<Guid>(1), chunk.Number) && owners.Count == 2)
                    {
                        bothClaimed.TrySetResult();
                    }

                    await bothClaimed.Task.WaitAsync(TimeSpan.FromSeconds(10)).ContinueWith(_ => { }, TaskScheduler.Default);

                    return chunk;
                });

            return spy;
        });
    }

    /// <summary>A clock from the app's time that moves a second on every read, so that every result is saved on its own.</summary>
    private static FakeTimeProvider Ticking(ProfitFoxFactory factory)
    {
        return new FakeTimeProvider(factory.Time.GetUtcNow()) { AutoAdvanceAmount = TimeSpan.FromSeconds(1) };
    }

    /// <summary>Waits for the job to finish, moving the app's clock a second at a time so that leases expire and waits pass.</summary>
    private static async Task<CandleImportJob> WaitUntilFinishedAsync(ProfitFoxFactory factory, ICandleImportJobRepository jobs, Guid id)
    {
        var waiting = Stopwatch.StartNew();

        while (true)
        {
            CandleImportJob job = (await jobs.GetAsync(id, CancellationToken.None))!;

            if (job.Status is CandleImportStatus.Succeeded or CandleImportStatus.Failed || waiting.Elapsed > TimeSpan.FromSeconds(30))
            {
                return job;
            }

            factory.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }
    }

    /// <summary>Three drops by four rises: twelve combinations.</summary>
    private static Dictionary<string, ParameterSpec> Grid()
    {
        return new Dictionary<string, ParameterSpec>
        {
            ["priceDropForBuy"] = ParameterSpec.Between(new NumberValue(0.01m), new NumberValue(0.03m), new NumberValue(0.01m)),
            ["priceRiseForSell"] = ParameterSpec.Between(new NumberValue(0.005m), new NumberValue(0.02m), new NumberValue(0.005m))
        };
    }

    private static DateTimeOffset At(string time)
    {
        return DateTimeOffset.Parse(time);
    }
}