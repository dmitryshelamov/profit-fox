using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Runs.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Backtests;

namespace ProfitFox.Tests.Integration.Worker.BackgroundServices;

/// <summary>
///     Change add-strategy-backtest, task 7.3: spec bench-run-queue with Workers of the test process on backtests —
///     «Состав стенда» («Второй Worker»), «Ошибка прогона», «Ход прогона», «Отмена прогона» and «Прерванный прогон» — over
///     four weeks of three pairs of the fake market-data service, with leases of 2 s renewed twice a second.
/// </summary>
[NonParallelizable]
public sealed class BacktestWorkerTests
{
    private readonly List<WorkerHost> _workers = [];
    private MarketDataServer _server = null!;
    private IBacktestRepository _backtests = null!;

    [SetUp]
    public async Task CreateBench()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        await BenchDatabase.Reset();
        _server = await MarketDataServer.Start();
        _backtests = BenchServices.Get<IBacktestRepository>();
    }

    [TearDown]
    public async Task StopWorkers()
    {
        foreach (WorkerHost worker in _workers)
        {
            await worker.DisposeAsync();
        }

        await _server.DisposeAsync();
        FakeMarketData.CoveredUntil = null;
    }

    [Test]
    public async Task A_queued_backtest_is_computed_to_its_numbers_by_a_Worker()
    {
        await StartWorker("w-1");
        Backtest backtest = await Enqueue();

        Backtest done = await Ended(backtest.Id);

        Assert.That((done.Status, done.LostLeases, done.PairsDone, done.PairsTotal), Is.EqualTo((BacktestState.Done, 0, 3, (int?)3)));
        Assert.That(done.Sets.Single().Numbers!.Trades, Is.GreaterThan(0));
    }

    /// <summary>
    ///     A Worker with <c>Worker:TakeRuns</c> = <c>false</c> keeps only the scanner (production: the scanner on
    ///     firebat-01, the runs on firebat-02): it leaves a queued backtest to a Worker that takes runs.
    /// </summary>
    [Test]
    public async Task A_Worker_that_takes_no_runs_leaves_a_queued_backtest_to_another_Worker()
    {
        _workers.Add(await WorkerHost.Start(_server, "scanner-only", runs: false));
        Backtest backtest = await Enqueue();

        await Task.Delay(TimeSpan.FromSeconds(2));
        Backtest waiting = await Read(backtest.Id);
        await StartWorker("w-1");
        Backtest done = await Ended(backtest.Id);

        Assert.That((waiting.Status, waiting.LeaseOwner), Is.EqualTo((BacktestState.Queued, (string?)null)));
        Assert.That((done.Status, done.LeaseOwner), Is.EqualTo((BacktestState.Done, "w-1")));
    }

    [Test]
    public async Task Two_Workers_go_two_backtests_at_the_same_time_each_its_own()
    {
        TaskCompletionSource bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrived = 0;
        _server.Data.BeforeCandles = async (_, ct) =>
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                bothArrived.TrySetResult();
            }

            await bothArrived.Task.WaitAsync(ct);
        };
        await StartWorker("w-1");
        await StartWorker("w-2");
        Backtest first = await Enqueue();
        Backtest second = await Enqueue();

        await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(30));

        string[] owners = [(await Read(first.Id)).LeaseOwner!, (await Read(second.Id)).LeaseOwner!];
        Assert.That(owners, Is.EquivalentTo(new[] { "w-1", "w-2" }), "a backtest does not wait for another of the same strategy");
        Assert.That(((await Ended(first.Id)).Status, (await Ended(second.Id)).Status), Is.EqualTo((BacktestState.Done, BacktestState.Done)));
    }

    [Test]
    public async Task A_backtest_in_the_middle_shows_the_pairs_it_has_passed_and_its_Worker()
    {
        (Task reached, Action release) = Hold("eth");
        await StartWorker("w-mid");
        Backtest backtest = await Enqueue();
        await reached;

        await Poll.Until("the progress of the backtest", async () => (await Read(backtest.Id)) is { PairsDone: 1, PairsTotal: 3 });

        Backtest middle = await Read(backtest.Id);
        Assert.That((middle.Status, middle.LeaseOwner, middle.StartedAt is not null), Is.EqualTo((BacktestState.Running, (string?)"w-mid", true)));
        release();
        Assert.That((await Ended(backtest.Id)).Status, Is.EqualTo(BacktestState.Done));
    }

    [Test]
    public async Task A_backtest_whose_history_stream_broke_is_failed_without_a_second_try_and_can_be_queued_again()
    {
        _server.Data.Failure = Grpc.Core.StatusCode.Unavailable;
        await StartWorker("w-1");
        Backtest backtest = await Enqueue();

        Backtest failed = await Ended(backtest.Id);
        int reads = _server.Data.CandleRequests.Count;
        await Task.Delay(WorkerHost.Lease + WorkerHost.Renewal);

        Assert.That((failed.Status, failed.Reason), Is.EqualTo((BacktestState.Failed, (string?)failed.Reason)));
        Assert.That(failed.Reason, Does.StartWith("история недоступна"));
        Assert.That(_server.Data.CandleRequests, Has.Count.EqualTo(reads), "a failed backtest is not taken again");
        _server.Data.Failure = null;
        Assert.That((await Ended((await Enqueue()).Id)).Status, Is.EqualTo(BacktestState.Done));
    }

    [Test]
    public async Task A_going_backtest_cancelled_stops_cancelled_within_seconds_and_can_be_queued_again()
    {
        (Task reached, Action _) = Hold("eth");
        await StartWorker("w-1");
        Backtest backtest = await Enqueue();
        await reached;

        CancelOutcome outcome = await _backtests.CancelAsync(backtest.Id, CancellationToken.None);
        TimeSpan took = await Poll.Until("the cancelled backtest", async () => (await Read(backtest.Id)).Status == BacktestState.Cancelled);

        Backtest cancelled = await Read(backtest.Id);
        Assert.That(outcome, Is.EqualTo(CancelOutcome.Requested));
        Assert.That(took, Is.LessThan(TimeSpan.FromSeconds(15)));
        Assert.That(cancelled.Reason, Does.StartWith("отменён: пройдено"));
        Assert.That(cancelled.Sets.Single().Numbers, Is.Null);
        _server.Data.BeforeCandles = null;
        Assert.That((await Ended((await Enqueue()).Id)).Status, Is.EqualTo(BacktestState.Done));
    }

    [Test]
    public async Task A_backtest_that_lost_its_lease_the_third_time_is_failed_and_not_computed()
    {
        Backtest backtest = await Enqueue();
        await Execute("UPDATE backtests SET status = 'running', lease_owner = 'gone', leased_until = now() - interval '1 second', lost_leases = 2 WHERE id = @Id", backtest.Id);
        await StartWorker("w-1");

        Backtest failed = await Ended(backtest.Id);

        Assert.That((failed.Status, failed.Reason, failed.LostLeases), Is.EqualTo((BacktestState.Failed, (string?)"прерван 3 раза", 3)));
        Assert.That(_server.Data.CandleRequests, Is.Empty);
    }

    private async Task StartWorker(string name)
    {
        _workers.Add(await WorkerHost.Start(_server, name));
    }

    private Task<Backtest> Enqueue()
    {
        Backtest backtest = BacktestGenerator.Generate();
        BacktestSettings settings = backtest.Settings with { End = FakeMarketData.CoveredFrom.AddDays(28) };
        return _backtests.EnqueueAsync(
            new Backtest
            {
                Id = backtest.Id,
                StrategyId = backtest.StrategyId,
                StrategyName = backtest.StrategyName,
                StrategyVersion = backtest.StrategyVersion,
                Kind = backtest.Kind,
                Graph = backtest.Graph,
                Settings = settings,
                Sets = backtest.Sets,
                SetCount = backtest.SetCount
            },
            CancellationToken.None);
    }

    private async Task<Backtest> Read(string id)
    {
        return (await _backtests.FindAsync(id, CancellationToken.None))!;
    }

    private async Task<Backtest> Ended(string id)
    {
        await Poll.Until($"the end of backtest {id}", async () => !(await Read(id)).IsActive);
        return await Read(id);
    }

    // The backtest holds on the first candle request of a pair, until it is released or the Worker drops the call.
    private (Task Reached, Action Release) Hold(string instrument)
    {
        TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Data.BeforeCandles = async (request, ct) =>
        {
            if (request.InstrumentIds.Single() == instrument)
            {
                reached.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        };
        return (reached.Task, () => release.TrySetResult());
    }

    private static async Task Execute(string sql, string id)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, new { Id = id });
    }
}
