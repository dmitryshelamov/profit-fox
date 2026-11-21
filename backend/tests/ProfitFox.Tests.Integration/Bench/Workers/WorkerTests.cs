using Dapper;
using Npgsql;
using ProfitFox.Application.Hypotheses;
using ProfitFox.Application.Runs;
using ProfitFox.Domain.Ledger;
using ProfitFox.Domain.Runs;
using ProfitFox.Infrastructure.Hypotheses;
using ProfitFox.Infrastructure.Ledger;
using ProfitFox.Infrastructure.Persistence;
using ProfitFox.Infrastructure.Runs;
using ProfitFox.Tests.Integration.Bench.Database;
using ProfitFox.Tests.Integration.Bench.MarketData;

namespace ProfitFox.Tests.Integration.Bench.Workers;

/// <summary>
///     Spec bench-run-queue with Workers in the test process over the test database and the fake market-data service: H0001
///     on four weeks of three pairs. The leases are short (2 s, renewed twice a second), the rules are the ones of production.
/// </summary>
[NonParallelizable]
public sealed class WorkerTests
{
    private readonly RunLedger _ledger = new RunLedger(BenchDatabase.DataSource);
    private readonly List<WorkerHost> _workers = [];
    private MarketDataServer _server = null!;
    private RunScheduler _scheduler = null!;

    [SetUp]
    public async Task CreateBench()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        await BenchDatabase.Reset();
        _server = await MarketDataServer.Start();
        var hypotheses = new HypothesisCatalog(new HypothesisStore(BenchDatabase.DataSource));
        await hypotheses.Add(BenchRepository.H0001Text(), CancellationToken.None);
        _scheduler = new RunScheduler(hypotheses, _ledger, new RunQueue(BenchDatabase.DataSource));
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
    public async Task A_queued_run_is_computed_to_its_line_and_report_by_a_Worker()
    {
        await StartWorker("w-1");
        RunRecord run = await Enqueue();

        await Finished(run.Id);

        LedgerEntry line = (await _ledger.Entries(CancellationToken.None)).Single();
        Assert.That((line.RunId, line.Status, line.Version, line.EngineVersion), Is.EqualTo((run.Id, RunStatus.InsufficientData, 1, EngineVersion.Number)));
        Assert.That(await Scalar<string>("SELECT text FROM reports WHERE run_id = @Id", run.Id), Does.Contain("Данных мало"));
        Assert.That((await Snapshot(run.Id)).LostLeases, Is.Zero);
    }

    [Test]
    public async Task A_run_in_the_middle_shows_the_pairs_it_has_passed_and_the_Worker_it_goes_at()
    {
        (Task reached, Action release) = Hold("eth");
        await StartWorker("w-mid");
        RunRecord run = await Enqueue();
        await reached;

        // The renewal writes the progress at least every 10 s in production, here every half second.
        await Poll.Until("the progress of the run", async () => (await Snapshot(run.Id)) is { PairsDone: 1, PairsTotal: 3 });

        RunSnapshot middle = await Snapshot(run.Id);
        Assert.That((middle.Status, middle.LeaseOwner, middle.PairsDone, middle.PairsTotal), Is.EqualTo((RunState.Running, "w-mid", 1, (int?)3)));
        release();
        await Finished(run.Id);
        Assert.That((await Snapshot(run.Id)).PairsDone, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task A_run_whose_Worker_stopped_is_taken_by_another_from_the_first_pair_and_has_one_line()
    {
        (Task reached, Action _) = Hold("eth");
        WorkerHost first = await StartWorker("w-1");
        RunRecord run = await Enqueue();
        await reached;
        await first.DisposeAsync();
        _workers.Remove(first);
        Assert.That(await Count("ledger"), Is.Zero, "a Worker that is stopped writes nothing");
        Assert.That((await Snapshot(run.Id)).Status, Is.EqualTo(RunState.Running), "its lease is left to expire");
        int btcReads = BtcReads();
        _server.Data.BeforeCandles = null;

        await StartWorker("w-2");
        await Finished(run.Id);

        RunSnapshot taken = await Snapshot(run.Id);
        Assert.That((taken.LeaseOwner, taken.LostLeases), Is.EqualTo(("w-2", 1)));
        LedgerEntry line = (await _ledger.Entries(CancellationToken.None)).Single();
        Assert.That((line.RunId, line.Status), Is.EqualTo((run.Id, RunStatus.InsufficientData)), "the interrupted run ends with its verdict, not with failed");
        Assert.That(BtcReads(), Is.GreaterThan(btcReads), "the run starts again with the first pair");
    }

    [Test]
    public async Task A_run_that_lost_its_lease_the_third_time_is_failed_and_not_computed()
    {
        RunRecord run = await Enqueue();
        await Execute("UPDATE runs SET status = 'running', lease_owner = 'dead', leased_until = now() - interval '1 second', lost_leases = 2 WHERE id = @Id", run.Id);

        await StartWorker("w-1");
        await Finished(run.Id);

        LedgerEntry line = (await _ledger.Entries(CancellationToken.None)).Single();
        Assert.That((line.RunId, line.Status, line.Reason), Is.EqualTo((run.Id, RunStatus.Failed, "прерван 3 раза")));
        Assert.That(await Count("reports"), Is.Zero);
        Assert.That(_server.Data.CandleRequests, Is.Empty, "the run is not computed");
        await Task.Delay(500);
        Assert.That(await Count("ledger"), Is.EqualTo(1), "and not taken again");
    }

    [Test]
    public async Task A_run_that_lost_its_lease_twice_is_still_computed()
    {
        RunRecord run = await Enqueue();
        await Execute("UPDATE runs SET status = 'running', lease_owner = 'dead', leased_until = now() - interval '1 second', lost_leases = 1 WHERE id = @Id", run.Id);

        await StartWorker("w-1");
        await Finished(run.Id);

        Assert.That((await _ledger.Entries(CancellationToken.None)).Single().Status, Is.EqualTo(RunStatus.InsufficientData));
        Assert.That((await Snapshot(run.Id)).LostLeases, Is.EqualTo(2));
    }

    [Test]
    public async Task A_run_whose_history_stream_broke_is_failed_without_a_second_try_and_can_be_queued_again()
    {
        _server.Data.Failure = Grpc.Core.StatusCode.Unavailable;
        await StartWorker("w-1");
        RunRecord run = await Enqueue();

        await Finished(run.Id);

        LedgerEntry line = (await _ledger.Entries(CancellationToken.None)).Single();
        Assert.That((line.RunId, line.Status), Is.EqualTo((run.Id, RunStatus.Failed)));
        Assert.That(line.Reason, Does.Contain("market-data"));
        Assert.That(await Count("reports"), Is.Zero);
        int requests = _server.Data.CandleRequests.Count;
        await Task.Delay(700);
        Assert.That(_server.Data.CandleRequests, Has.Count.EqualTo(requests), "nobody takes it again");
        Assert.That((await Snapshot(run.Id)).LostLeases, Is.Zero);

        _server.Data.Failure = null;
        RunRecord again = await Enqueue();
        await Finished(again.Id);
        Assert.That((await _ledger.Entries(CancellationToken.None)).Select(entry => entry.Status), Is.EqualTo(new[] { RunStatus.Failed, RunStatus.InsufficientData }));
    }

    [Test]
    public async Task A_run_that_is_cancelled_stops_within_a_renewal_and_has_a_cancelled_line_and_can_be_queued_again()
    {
        (Task reached, Action _) = Hold("eth");
        await StartWorker("w-1");
        RunRecord run = await Enqueue();
        await reached;
        await Poll.Until("the first pair", async () => (await Snapshot(run.Id)).PairsDone == 1);

        CancelOutcome outcome = await _scheduler.Cancel(run.Id, CancellationToken.None);
        TimeSpan stopped = await Poll.Until("the cancelled line", async () => await Count("ledger") == 1);

        Assert.That(outcome, Is.EqualTo(CancelOutcome.Requested));
        LedgerEntry line = (await _ledger.Entries(CancellationToken.None)).Single();
        Assert.That((line.RunId, line.Status), Is.EqualTo((run.Id, RunStatus.Cancelled)));
        Assert.That(line.Reason, Does.Contain("пройдено 1 пар из 3"));
        Assert.That(stopped, Is.LessThan(WorkerHost.Renewal + TimeSpan.FromSeconds(3)), "a renewal and a little more; production has 10 s for a renewal and 15 s in all");
        Assert.That((await Snapshot(run.Id)).Status, Is.EqualTo(RunState.Finished));
        Assert.That(await Count("reports"), Is.Zero);

        _server.Data.BeforeCandles = null;
        await Finished((await Enqueue()).Id);
        Assert.That((await _ledger.Entries(CancellationToken.None)).Select(entry => entry.Status), Is.EqualTo(new[] { RunStatus.Cancelled, RunStatus.InsufficientData }), "a cancelled run is not final");
    }

    [Test]
    public async Task A_run_cancelled_while_its_Worker_was_dead_ends_cancelled_without_being_computed()
    {
        RunRecord run = await Enqueue();
        await Execute("UPDATE runs SET status = 'running', lease_owner = 'dead', leased_until = now() - interval '1 second', cancel_requested = true WHERE id = @Id", run.Id);

        await StartWorker("w-1");
        await Finished(run.Id);

        Assert.That((await _ledger.Entries(CancellationToken.None)).Single().Status, Is.EqualTo(RunStatus.Cancelled));
        Assert.That(_server.Data.CandleRequests, Is.Empty);
    }

    [Test]
    public async Task Two_Workers_go_two_runs_at_the_same_time_each_its_own()
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
        RunRecord verdict = await Enqueue();
        RunRecord parity = await Enqueue(parity: true);

        await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(30));

        string[] owners = [(await Snapshot(verdict.Id)).LeaseOwner!, (await Snapshot(parity.Id)).LeaseOwner!];
        Assert.That(owners, Is.EquivalentTo(new[] { "w-1", "w-2" }));
        await Finished(verdict.Id);
        await Finished(parity.Id);
        Assert.That((await _ledger.Entries(CancellationToken.None)).Select(entry => entry.Status), Is.EquivalentTo(new[] { RunStatus.InsufficientData, RunStatus.Parity }));
    }

    [Test]
    public async Task A_Worker_goes_one_run_at_a_time()
    {
        (Task reached, Action release) = Hold("btc");
        await StartWorker("w-1");
        RunRecord first = await Enqueue();
        RunRecord second = await Enqueue(parity: true);
        await reached;

        await Task.Delay(700);

        Assert.That((await Snapshot(first.Id)).Status, Is.EqualTo(RunState.Running));
        Assert.That((await Snapshot(second.Id)).Status, Is.EqualTo(RunState.Queued));
        _server.Data.BeforeCandles = null;
        release();
        await Finished(second.Id);
        Assert.That(await Count("ledger"), Is.EqualTo(2));
    }

    [Test]
    public async Task A_Worker_that_lost_the_lease_stops_and_writes_nothing_though_its_run_could_end()
    {
        (Task reached, Action release) = Hold("eth");
        await StartWorker("w-1");
        RunRecord run = await Enqueue();
        await reached;
        await Execute("UPDATE runs SET lease_owner = 'w-other', leased_until = now() + interval '1 hour' WHERE id = @Id", run.Id);

        _server.Data.BeforeCandles = null;
        release();
        await Task.Delay(WorkerHost.Renewal + TimeSpan.FromSeconds(2));

        Assert.That(await Count("ledger"), Is.Zero);
        Assert.That(await Count("reports"), Is.Zero);
        RunSnapshot row = await Snapshot(run.Id);
        Assert.That((row.Status, row.LeaseOwner), Is.EqualTo((RunState.Running, "w-other")));
    }

    [Test]
    public async Task A_queued_run_the_ledger_now_refuses_ends_with_a_failed_line()
    {
        RunRecord run = await Enqueue();
        LedgerEntry verdict = new LedgerEntry
        {
            RunId = Guid.NewGuid().ToString("N"),
            Time = DateTimeOffset.UtcNow,
            Hypothesis = "H0001",
            Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Version = 1,
            EngineVersion = EngineVersion.Number,
            Period = "exploration",
            Status = RunStatus.Rejected
        };
        await _ledger.Decide(_ => new Decision(verdict, Report: null), CancellationToken.None);

        await StartWorker("w-1");
        await Finished(run.Id);

        LedgerEntry[] lines = [.. await _ledger.Entries(CancellationToken.None)];
        Assert.That(lines.Select(line => line.Status), Is.EqualTo(new[] { RunStatus.Rejected, RunStatus.Failed }));
        Assert.That(lines[1].Reason, Does.Contain("already has the verdict"));
        Assert.That(_server.Data.CandleRequests, Is.Empty, "no history is read for a refused run");
    }

    [Test]
    public async Task A_Worker_waits_for_the_schema_and_goes_a_run_once_the_database_has_it()
    {
        string connectionString = await BenchDatabase.NewDatabase();
        await using NpgsqlDataSource database = NpgsqlDataSource.Create(connectionString);
        await StartWorker("w-1", connectionString);
        await Task.Delay(700);
        Assert.That(await BenchSchema.Current(database, CancellationToken.None), Is.Zero, "the API has not migrated yet");

        await using (BenchDatabase.Migrated(connectionString))
        {
        }

        var hypotheses = new HypothesisCatalog(new HypothesisStore(database));
        await hypotheses.Add(BenchRepository.H0001Text(), CancellationToken.None);
        var scheduler = new RunScheduler(hypotheses, new RunLedger(database), new RunQueue(database));
        RunRecord run = await scheduler.Enqueue("H0001", version: null, Period.Exploration, parity: false, CancellationToken.None);

        await Poll.Until("the line of the run", async () => (await new RunLedger(database).Entries(CancellationToken.None)).Any(line => line.RunId == run.Id));
    }

    private Task<RunRecord> Enqueue(bool parity = false)
    {
        return _scheduler.Enqueue("H0001", version: null, Period.Exploration, parity, CancellationToken.None);
    }

    private async Task<WorkerHost> StartWorker(string name, string? connectionString = null)
    {
        WorkerHost worker = await WorkerHost.Start(_server, name, connectionString);
        _workers.Add(worker);
        return worker;
    }

    // The run holds on the first candle request of a pair, until it is released or the Worker drops the call.
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

    private int BtcReads()
    {
        lock (_server.Data.CandleRequests)
        {
            return _server.Data.CandleRequests.Count(request => request.InstrumentIds.Single() == "btc");
        }
    }

    private static Task Finished(string runId)
    {
        return Poll.Until($"the end of run {runId}", async () => (await Snapshot(runId)).Status == RunState.Finished);
    }

    private static async Task<RunSnapshot> Snapshot(string runId)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<RunSnapshot>(
            """
            SELECT status AS "Status", lease_owner AS "LeaseOwner", lost_leases AS "LostLeases", pairs_done AS "PairsDone", pairs_total AS "PairsTotal"
            FROM runs WHERE id = @Id
            """,
            new { Id = runId });
    }

    private static async Task Execute(string sql, string id)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, new { Id = id });
    }

    private static async Task<T?> Scalar<T>(string sql, string id)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<T>(sql, new { Id = id });
    }

    private static async Task<int> Count(string table)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table}");
    }

    private sealed record RunSnapshot(string Status, string? LeaseOwner, int LostLeases, int PairsDone, int? PairsTotal);
}
