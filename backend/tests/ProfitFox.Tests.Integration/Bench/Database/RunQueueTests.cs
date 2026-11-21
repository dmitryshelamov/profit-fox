using Dapper;
using Npgsql;
using ProfitFox.Application.Hypotheses;
using ProfitFox.Application.Runs;
using ProfitFox.Domain.Ledger;
using ProfitFox.Domain.Runs;
using ProfitFox.Infrastructure.Hypotheses;
using ProfitFox.Infrastructure.Ledger;
using ProfitFox.Infrastructure.Runs;

namespace ProfitFox.Tests.Integration.Bench.Database;

/// <summary>
///     Spec bench-run-queue on the database: the refusals at queueing («Прогон после вердикта», «Двойная постановка»), the
///     order of the queue and the lease («Порядок очереди», «Аренда прогона»), the cancelling of a run («Отмена прогона») and
///     the end of a run in one step with its line («Результат прогона»).
/// </summary>
public sealed class RunQueueTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private readonly RunLedger _ledger = new RunLedger(BenchDatabase.DataSource);
    private readonly RunQueue _queue = new RunQueue(BenchDatabase.DataSource);
    private RunScheduler _scheduler = null!;

    [SetUp]
    public async Task CreateQueue()
    {
        await BenchDatabase.Reset();
        var hypotheses = new HypothesisCatalog(new HypothesisStore(BenchDatabase.DataSource));
        await hypotheses.Add(BenchRepository.H0001Text(), CancellationToken.None);
        _scheduler = new RunScheduler(hypotheses, _ledger, _queue);
    }

    [Test]
    public async Task A_run_is_queued_waiting_with_the_latest_version()
    {
        RunRecord run = await Enqueue(Period.Exploration);

        Assert.That((run.Hypothesis, run.Version, run.Period, run.Parity, run.Status), Is.EqualTo(("H0001", 1, "exploration", false, RunState.Queued)));
        Assert.That((run.LostLeases, run.CancelRequested, run.PairsDone, run.LeaseOwner), Is.EqualTo((0, false, 0, (string?)null)));
        Assert.That(run.Id, Has.Length.EqualTo(32));
    }

    [Test]
    public async Task A_version_with_a_verdict_is_refused_with_the_run_of_the_verdict_and_the_queue_stays_empty()
    {
        LedgerEntry verdict = Line(RunStatus.Rejected);
        await _ledger.Decide(_ => new Decision(verdict, Report: null), CancellationToken.None);

        Assert.That(() => Enqueue(Period.Exploration), Throws.InstanceOf<LedgerException>().With.Message.Contains("already has the verdict rejected").And.Property(nameof(LedgerException.RunId)).EqualTo(verdict.RunId));

        Assert.That(await Count("runs"), Is.Zero);
    }

    [Test]
    public async Task The_holdout_is_refused_before_exploration_is_passed_and_a_parity_run_goes_on_exploration_only()
    {
        Assert.That(() => Enqueue(Period.Holdout), Throws.InstanceOf<LedgerException>().With.Message.Contains("holdout opens only after"));
        Assert.That(() => Enqueue(Period.Holdout, parity: true), Throws.InstanceOf<LedgerException>().With.Message.Contains("parity run reads only exploration"));

        RunRecord parity = await Enqueue(Period.Exploration, parity: true);

        Assert.That(parity.Parity, Is.True);
    }

    [Test]
    public async Task A_run_of_the_same_version_period_and_kind_that_waits_is_refused_with_the_waiting_run()
    {
        RunRecord waiting = await Enqueue(Period.Exploration);

        Assert.That(() => Enqueue(Period.Exploration), Throws.InstanceOf<LedgerException>().With.Message.Contains("waits in the queue").And.Property(nameof(LedgerException.RunId)).EqualTo(waiting.Id));

        Assert.That(await Count("runs"), Is.EqualTo(1));
        await Enqueue(Period.Exploration, parity: true);
        await Enqueue(Period.NewData);
        Assert.That(await Count("runs"), Is.EqualTo(3), "another kind and another period are other runs");
    }

    [Test]
    public async Task A_run_that_goes_is_refused_as_well_and_a_run_that_ended_is_not()
    {
        RunRecord run = await Enqueue(Period.Exploration);
        await _queue.Claim("w1", Lease, CancellationToken.None);

        Assert.That(() => Enqueue(Period.Exploration), Throws.InstanceOf<LedgerException>().With.Message.Contains("is going").And.Property(nameof(LedgerException.RunId)).EqualTo(run.Id));

        await _ledger.Decide(_ => new Decision(Line(RunStatus.Failed) with { RunId = run.Id }, Report: null), new RunAssignment(run.Id, "w1"), CancellationToken.None);
        Assert.That((await Enqueue(Period.Exploration)).Id, Is.Not.EqualTo(run.Id));
    }

    [Test]
    public async Task Two_requests_that_come_together_queue_one_run_and_the_other_is_refused_with_it()
    {
        Task<RunRecord>[] requests = [.. Enumerable.Range(0, 8).Select(_ => Task.Run(() => Enqueue(Period.Exploration)))];
        await Task.WhenAll(requests.Select(request => request.ContinueWith(_ => { }, TaskScheduler.Default)));

        RunRecord[] queued = [.. requests.Where(request => request.IsCompletedSuccessfully).Select(request => request.Result)];
        LedgerException[] refused = [.. requests.Where(request => request.IsFaulted).Select(request => (LedgerException)request.Exception!.InnerException!)];
        Assert.That(queued, Has.Length.EqualTo(1));
        Assert.That(refused, Has.Length.EqualTo(7));
        Assert.That(refused.Select(exception => exception.RunId), Has.All.EqualTo(queued[0].Id));
        Assert.That(await Count("runs"), Is.EqualTo(1));
    }

    [Test]
    public async Task Runs_are_taken_in_the_order_they_were_queued_and_a_run_is_taken_once()
    {
        RunRecord first = await Enqueue(Period.Exploration);
        RunRecord second = await Enqueue(Period.Exploration, parity: true);
        RunRecord third = await Enqueue(Period.NewData);

        RunRecord? a = await _queue.Claim("w1", Lease, CancellationToken.None);
        RunRecord? b = await _queue.Claim("w2", Lease, CancellationToken.None);

        Assert.That((a!.Id, a.LeaseOwner, a.Status, a.LostLeases), Is.EqualTo((first.Id, "w1", RunState.Running, 0)));
        Assert.That((b!.Id, b.LeaseOwner), Is.EqualTo((second.Id, "w2")));
        Assert.That(a.StartedAt, Is.Not.Null);
        Assert.That(a.LeasedUntil - a.StartedAt, Is.EqualTo(Lease).Within(TimeSpan.FromSeconds(1)), "the lease is counted from the time of the database");
        Assert.That((await _queue.Claim("w3", Lease, CancellationToken.None))!.Id, Is.EqualTo(third.Id));
        Assert.That(await _queue.Claim("w4", Lease, CancellationToken.None), Is.Null, "everything is taken");
    }

    [Test]
    public async Task Workers_that_take_runs_together_never_take_the_same_one()
    {
        RunRecord[] queued = [await Enqueue(Period.Exploration), await Enqueue(Period.Exploration, parity: true), await Enqueue(Period.NewData)];

        RunRecord?[] taken = await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() => _queue.Claim($"w{index}", Lease, CancellationToken.None))));

        Assert.That(taken.Where(run => run is not null).Select(run => run!.Id), Is.EquivalentTo(queued.Select(run => run.Id)), "every run once, and no Worker got a run twice");
        Assert.That(taken.Count(run => run is null), Is.EqualTo(9));
    }

    [Test]
    public async Task A_run_whose_lease_expired_is_taken_again_with_a_lost_lease_counted_and_a_lease_that_holds_is_not()
    {
        RunRecord run = await Enqueue(Period.Exploration);
        await _queue.Claim("w1", Lease, CancellationToken.None);
        await _queue.Renew(run.Id, "w1", Lease, pairsDone: 300, pairsTotal: 828, CancellationToken.None);

        Assert.That(await _queue.Claim("w2", Lease, CancellationToken.None), Is.Null, "the lease of w1 holds");
        await Execute("UPDATE runs SET leased_until = now() - interval '1 second' WHERE id = @Id", run.Id);
        RunRecord? again = await _queue.Claim("w2", Lease, CancellationToken.None);

        Assert.That((again!.Id, again.LeaseOwner, again.LostLeases, again.PairsDone, again.PairsTotal), Is.EqualTo((run.Id, "w2", 1, 0, (int?)null)), "it starts again from the first pair");
        Assert.That(await _queue.Renew(run.Id, "w1", Lease, 400, 828, CancellationToken.None), Is.Null, "w1 lost the run");
    }

    [Test]
    public async Task A_renewal_extends_the_lease_records_the_progress_and_tells_when_the_run_is_cancelled()
    {
        RunRecord run = await Enqueue(Period.Exploration);
        await _queue.Claim("w1", TimeSpan.FromSeconds(30), CancellationToken.None);

        Renewal? renewal = await _queue.Renew(run.Id, "w1", Lease, pairsDone: 414, pairsTotal: 828, CancellationToken.None);

        Assert.That(renewal, Is.EqualTo(new Renewal(CancelRequested: false)));
        Assert.That(await Scalar<int>("SELECT pairs_done FROM runs WHERE id = @Id", run.Id), Is.EqualTo(414));
        Assert.That(await Scalar<int>("SELECT pairs_total FROM runs WHERE id = @Id", run.Id), Is.EqualTo(828));
        Assert.That(await Scalar<double>("SELECT extract(epoch FROM leased_until - now()) FROM runs WHERE id = @Id", run.Id), Is.GreaterThan(250), "five minutes from now, not 30 seconds");
        await _queue.Cancel(run.Id, CancellationToken.None);
        Assert.That(await _queue.Renew(run.Id, "w1", Lease, 415, 828, CancellationToken.None), Is.EqualTo(new Renewal(CancelRequested: true)));
    }

    [Test]
    public async Task A_run_nobody_holds_cannot_be_renewed()
    {
        RunRecord waiting = await Enqueue(Period.Exploration);

        Assert.That(await _queue.Renew(waiting.Id, "w1", Lease, 0, 828, CancellationToken.None), Is.Null);
        Assert.That(await _queue.Renew("no-such-run", "w1", Lease, 0, 828, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task A_waiting_run_that_is_cancelled_leaves_the_queue_without_a_line_in_the_ledger()
    {
        RunRecord run = await Enqueue(Period.Exploration);

        CancelOutcome outcome = await _scheduler.Cancel(run.Id, CancellationToken.None);

        Assert.That(outcome, Is.EqualTo(CancelOutcome.Removed));
        Assert.That(await Count("runs"), Is.Zero);
        Assert.That(await Count("ledger"), Is.Zero);
        Assert.That(await _queue.Claim("w1", Lease, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task A_running_run_that_is_cancelled_is_asked_to_stop_and_a_run_that_ended_or_is_unknown_is_refused()
    {
        RunRecord running = await Enqueue(Period.Exploration);
        await _queue.Claim("w1", Lease, CancellationToken.None);
        RunRecord ended = await Enqueue(Period.Exploration, parity: true);
        await _queue.Claim("w1", Lease, CancellationToken.None);
        await _ledger.Decide(_ => new Decision(Line(RunStatus.Failed) with { RunId = ended.Id }, Report: null), new RunAssignment(ended.Id, "w1"), CancellationToken.None);

        Assert.That(await _queue.Cancel(running.Id, CancellationToken.None), Is.EqualTo(CancelOutcome.Requested));
        Assert.That(await Scalar<bool>("SELECT cancel_requested FROM runs WHERE id = @Id", running.Id), Is.True);
        Assert.That(await _queue.Cancel(ended.Id, CancellationToken.None), Is.EqualTo(CancelOutcome.AlreadyFinished));
        Assert.That(await _queue.Cancel("no-such-run", CancellationToken.None), Is.EqualTo(CancelOutcome.NotFound));
        Assert.That(await Scalar<bool>("SELECT cancel_requested FROM runs WHERE id = @Id", ended.Id), Is.False);
    }

    [Test]
    public async Task The_line_the_report_and_the_end_of_a_run_appear_together()
    {
        RunRecord run = await Enqueue(Period.Exploration);
        await _queue.Claim("w1", Lease, CancellationToken.None);

        LedgerEntry written = await _ledger.Decide(_ => new Decision(Line(RunStatus.Rejected) with { RunId = run.Id }, "# report"), new RunAssignment(run.Id, "w1"), CancellationToken.None);

        Assert.That(written.RunId, Is.EqualTo(run.Id));
        Assert.That(await Scalar<string>("SELECT status FROM runs WHERE id = @Id", run.Id), Is.EqualTo(RunState.Finished));
        Assert.That(await Scalar<DateTime?>("SELECT finished_at FROM runs WHERE id = @Id", run.Id), Is.Not.Null);
        Assert.That(await Scalar<string>("SELECT text FROM reports WHERE run_id = @Id", run.Id), Is.EqualTo("# report"));
        Assert.That(await Count("ledger"), Is.EqualTo(1));
        Assert.That(await _queue.Claim("w2", Lease, CancellationToken.None), Is.Null, "a run that ended is not taken again");
    }

    [Test]
    public async Task A_Worker_that_lost_the_run_writes_nothing_and_its_decision_is_not_even_made()
    {
        RunRecord run = await Enqueue(Period.Exploration);
        await _queue.Claim("w1", Lease, CancellationToken.None);
        await Execute("UPDATE runs SET leased_until = now() - interval '1 second' WHERE id = @Id", run.Id);
        await _queue.Claim("w2", Lease, CancellationToken.None);
        bool decided = false;

        Assert.That(
            () => _ledger.Decide(_ =>
            {
                decided = true;
                return new Decision(Line(RunStatus.Rejected) with { RunId = run.Id }, "# report");
            }, new RunAssignment(run.Id, "w1"), CancellationToken.None),
            Throws.InstanceOf<LeaseLostException>());

        Assert.That(decided, Is.False);
        Assert.That(await Count("ledger"), Is.Zero);
        Assert.That(await Count("reports"), Is.Zero);
        Assert.That(await Scalar<string>("SELECT lease_owner FROM runs WHERE id = @Id", run.Id), Is.EqualTo("w2"));
        Assert.That(await Scalar<string>("SELECT status FROM runs WHERE id = @Id", run.Id), Is.EqualTo(RunState.Running));
    }

    [Test]
    public async Task A_run_that_already_ended_cannot_end_a_second_time()
    {
        RunRecord run = await Enqueue(Period.Exploration);
        await _queue.Claim("w1", Lease, CancellationToken.None);
        await _ledger.Decide(_ => new Decision(Line(RunStatus.Failed) with { RunId = run.Id }, Report: null), new RunAssignment(run.Id, "w1"), CancellationToken.None);

        Assert.That(
            () => _ledger.Decide(_ => new Decision(Line(RunStatus.Failed) with { RunId = run.Id }, Report: null), new RunAssignment(run.Id, "w1"), CancellationToken.None),
            Throws.InstanceOf<LeaseLostException>());

        Assert.That(await Count("ledger"), Is.EqualTo(1));
    }

    [Test]
    public async Task A_decision_that_throws_leaves_the_run_going_and_writes_nothing()
    {
        RunRecord run = await Enqueue(Period.Exploration);
        await _queue.Claim("w1", Lease, CancellationToken.None);

        Assert.That(
            () => _ledger.Decide(_ => throw new LedgerException("refused"), new RunAssignment(run.Id, "w1"), CancellationToken.None),
            Throws.InstanceOf<LedgerException>());

        Assert.That(await Scalar<string>("SELECT status FROM runs WHERE id = @Id", run.Id), Is.EqualTo(RunState.Running));
        Assert.That(await Count("ledger"), Is.Zero);
    }

    [Test]
    public async Task A_line_of_another_run_cannot_end_a_run()
    {
        RunRecord run = await Enqueue(Period.Exploration);
        await _queue.Claim("w1", Lease, CancellationToken.None);

        Assert.That(
            () => _ledger.Decide(_ => new Decision(Line(RunStatus.Failed), Report: null), new RunAssignment(run.Id, "w1"), CancellationToken.None),
            Throws.InstanceOf<InvalidOperationException>());

        Assert.That(await Count("ledger"), Is.Zero);
    }

    private Task<RunRecord> Enqueue(Period period, bool parity = false)
    {
        return _scheduler.Enqueue("H0001", version: null, period, parity, CancellationToken.None);
    }

    private static LedgerEntry Line(string status)
    {
        return new LedgerEntry
        {
            RunId = Guid.NewGuid().ToString("N"),
            Time = DateTimeOffset.UtcNow,
            Hypothesis = "H0001",
            Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Version = 1,
            EngineVersion = EngineVersion.Number,
            Period = "exploration",
            Status = status
        };
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
}
