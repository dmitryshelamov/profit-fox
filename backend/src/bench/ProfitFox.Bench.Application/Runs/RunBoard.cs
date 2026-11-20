using ProfitFox.Bench.Application.Hypotheses;
using ProfitFox.Bench.Domain.Ledger;
using ProfitFox.Bench.Domain.Runs;

namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     What the page and the CLI read about runs (spec bench-run-queue «Результат прогона», spec hypothesis-bench): the runs
///     that wait and go, the runs that ended, one run with its line and report, and for the buttons whether each version can be
///     run. It changes nothing.
/// </summary>
public sealed class RunBoard(IRunLedger ledger, IRunQueue queue)
{
    private static readonly Period[] AllPeriods = [Period.Exploration, Period.Holdout, Period.NewData];
    private static readonly bool[] Kinds = [false, true];

    /// <summary>
    ///     The runs that wait and go, the one queued first first.
    /// </summary>
    public Task<IReadOnlyList<RunRecord>> Active(CancellationToken cancellation)
    {
        return queue.Active(cancellation);
    }

    /// <summary>
    ///     The latest lines of the ledger, the newest first.
    /// </summary>
    public async Task<IReadOnlyList<FinishedRun>> Finished(int limit, CancellationToken cancellation)
    {
        return await ledger.Recent(limit, cancellation);
    }

    /// <summary>
    ///     The run <paramref name="runId" /> by its row in the queue and, once it ended, by its line in the ledger; null when the
    ///     queue has no such run.
    /// </summary>
    public async Task<RunDetails?> Find(string runId, CancellationToken cancellation)
    {
        RunRecord? run = await queue.Find(runId, cancellation);
        if (run is null)
        {
            return null;
        }

        LedgerResult? result = await ledger.Find(runId, cancellation);
        return new RunDetails(run, result?.Entry, result?.Report);
    }

    /// <summary>
    ///     For each hypothesis of the list, whether its version can be run now on each period in each kind, read once for all of
    ///     them. The reasons are those <see cref="RunScheduler.Enqueue" /> would refuse with; a hypothesis whose text has errors
    ///     is refused with them.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<RunAvailability>>> Availability(IReadOnlyList<HypothesisItem> items, CancellationToken cancellation)
    {
        IReadOnlyList<RunRecord> active = await queue.Active(cancellation);
        IReadOnlyList<LedgerEntry> entries = await ledger.Entries(cancellation);
        return items.ToDictionary(
            item => item.Id,
            item => item.Error is { } error
                ? Refused(error)
                : Availability(item.Id, item.Version, entries, active));
    }

    private static IReadOnlyList<RunAvailability> Refused(string reason)
    {
        return [.. AllPeriods.SelectMany(period => Kinds.Select(parity => new RunAvailability(Periods.Name(period), RunKinds.Name(parity), CanRun: false, reason, RunId: null)))];
    }

    private static IReadOnlyList<RunAvailability> Availability(
        string hypothesis,
        int version,
        IReadOnlyList<LedgerEntry> entries,
        IReadOnlyList<RunRecord> active)
    {
        List<RunAvailability> all = [];
        foreach (Period period in AllPeriods)
        {
            string name = Periods.Name(period);
            foreach (bool parity in Kinds)
            {
                string kind = RunKinds.Name(parity);
                RunRecord? standing = active.FirstOrDefault(run => run.Hypothesis == hypothesis && run.Version == version && run.Period == name && run.Parity == parity);
                if (standing is not null)
                {
                    string state = standing.Status == RunState.Queued ? "waits in the queue" : "is going";
                    all.Add(new RunAvailability(name, kind, CanRun: false, $"{hypothesis} version {version} on {name} already {state} (run {standing.Id}).", standing.Id));
                    continue;
                }

                try
                {
                    LedgerRules.CheckCanRun(entries, hypothesis, version, name, parity);
                    all.Add(new RunAvailability(name, kind, CanRun: true, Reason: null, RunId: null));
                }
                catch (LedgerException exception)
                {
                    all.Add(new RunAvailability(name, kind, CanRun: false, exception.Message, exception.RunId));
                }
            }
        }

        return all;
    }
}
