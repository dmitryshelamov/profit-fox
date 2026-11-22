using ProfitFox.Application.Contracts.Hypotheses;
using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Application.Queries.Runs;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Runs;

namespace ProfitFox.Tests.Unit.Application.Queries.Runs;

/// <summary>
///     Spec bench-run-queue «Последний прогон гипотезы» (change redesign-bench-ui, task 2.4), on a queue and a ledger in
///     memory: the run of the latest version that waits or goes, else the one that ended last.
/// </summary>
public sealed class GetLastRunsQueryHandlerTests
{
    private static readonly DateTimeOffset Time = new DateTimeOffset(2026, 11, 21, 0, 1, 0, TimeSpan.Zero);
    private readonly CancellationToken _none = CancellationToken.None;
    private MemoryRunRepository _runs = null!;
    private MemoryLedgerRepository _ledger = null!;

    [SetUp]
    public void Create()
    {
        _runs = new MemoryRunRepository();
        _ledger = new MemoryLedgerRepository();
    }

    [Test]
    public async Task A_finished_run()
    {
        _ledger.Add(Line("run-1", RunStatus.InsufficientData));

        LastRunInternal last = (await Last(Hypothesis("H0001", version: 1)))["H0001"];

        Assert.That(last.Active, Is.Null);
        Assert.That((last.Finished?.Entry.RunId, last.Finished?.Entry.Status, last.Finished?.Parity), Is.EqualTo(("run-1", RunStatus.InsufficientData, false)));
    }

    [Test]
    public async Task The_run_that_ended_last_of_two()
    {
        _ledger.Add(Line("run-1", RunStatus.Failed));
        _ledger.Add(Line("run-2", RunStatus.Parity) with { Time = Time.AddHours(1) }, parity: true);

        LastRunInternal last = (await Last(Hypothesis("H0001", version: 1)))["H0001"];

        Assert.That((last.Finished?.Entry.RunId, last.Finished?.Parity), Is.EqualTo(("run-2", true)));
    }

    [Test]
    public async Task A_run_in_the_queue_comes_before_a_finished_one()
    {
        _ledger.Add(Line("run-1", RunStatus.InsufficientData));
        _runs.Add(RunGenerator.Generate("run-1", status: RunState.Finished));
        _runs.Add(RunGenerator.Generate("run-2"));

        LastRunInternal last = (await Last(Hypothesis("H0001", version: 1)))["H0001"];

        Assert.That(last.Finished, Is.Null);
        Assert.That((last.Active?.Id, last.Active?.Status), Is.EqualTo(("run-2", RunState.Queued)));
    }

    [Test]
    public async Task Of_two_runs_in_the_queue_the_first()
    {
        _runs.Add(RunGenerator.Generate("run-1", parity: true, status: RunState.Running));
        _runs.Add(RunGenerator.Generate("run-2"));

        LastRunInternal last = (await Last(Hypothesis("H0001", version: 1)))["H0001"];

        Assert.That((last.Active?.Id, last.Active?.Status), Is.EqualTo(("run-1", RunState.Running)));
    }

    [Test]
    public async Task A_new_version_without_runs()
    {
        _ledger.Add(Line("run-1", RunStatus.Rejected));
        _runs.Add(RunGenerator.Generate("run-2", version: 1));

        IReadOnlyDictionary<string, LastRunInternal> last = await Last(Hypothesis("H0001", version: 2));

        Assert.That(last, Is.Empty, "the runs of version 1 are not the runs of version 2");
    }

    [Test]
    public async Task Each_hypothesis_has_its_own_run_and_the_ledger_and_the_queue_are_read_once()
    {
        _ledger.Add(Line("run-1", RunStatus.Passed));
        _ledger.Add(Line("run-2", RunStatus.Rejected) with { Hypothesis = "H0002" });
        _runs.Add(RunGenerator.Generate("run-3", hypothesis: "H0003"));

        IReadOnlyDictionary<string, LastRunInternal> last = await Last(Hypothesis("H0001", 1), Hypothesis("H0002", 1), Hypothesis("H0003", 1), Hypothesis("H0004", 1));

        Assert.That(last.Keys, Is.EquivalentTo(new[] { "H0001", "H0002", "H0003" }));
        Assert.That((last["H0001"].Finished?.Entry.RunId, last["H0002"].Finished?.Entry.RunId, last["H0003"].Active?.Id), Is.EqualTo(("run-1", "run-2", "run-3")));
        Assert.That((_ledger.LastReads, _runs.ActiveReads), Is.EqualTo((1, 1)));
    }

    private Task<IReadOnlyDictionary<string, LastRunInternal>> Last(params HypothesisInternal[] hypotheses)
    {
        return new GetLastRunsQueryHandler(_runs, _ledger).Handle(new GetLastRunsQuery(hypotheses), _none);
    }

    private static HypothesisInternal Hypothesis(string id, int version)
    {
        return new HypothesisInternal(id, version, "funding-short-4h", TimeSpan.FromHours(4), Signal: null, Error: null);
    }

    private static LedgerEntry Line(string runId, string status)
    {
        return new LedgerEntry
        {
            RunId = runId,
            Time = Time,
            Hypothesis = "H0001",
            Hash = new string('a', 64),
            Version = 1,
            EngineVersion = 1,
            Period = "exploration",
            Status = status
        };
    }
}
