using ProfitFox.Bench.Domain.Ledger;

namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     The ledger of runs (spec hypothesis-bench): its lines in the order they were written, only ever added. Its rules are
///     <see cref="LedgerRules" />.
/// </summary>
public interface IRunLedger
{
    Task<IReadOnlyList<LedgerEntry>> Entries(CancellationToken cancellation);

    /// <summary>
    ///     The latest <paramref name="limit" /> lines, the newest first, each with the kind of its run.
    /// </summary>
    Task<IReadOnlyList<FinishedRun>> Recent(int limit, CancellationToken cancellation);

    /// <summary>
    ///     The line of the run <paramref name="runId" /> with its report; null when the ledger has no line of it.
    /// </summary>
    Task<LedgerResult?> Find(string runId, CancellationToken cancellation);

    /// <summary>
    ///     Decides a run on the ledger as it is at that moment, and writes its line with its report as one step. The decisions of
    ///     all runs of the bench go one at a time, so each sees the lines written before it: the attempts of two runs that end
    ///     together are 1 and 2. Whatever <paramref name="decide" /> throws, nothing is written.
    /// </summary>
    /// <returns>The line that was written.</returns>
    Task<LedgerEntry> Decide(Func<IReadOnlyList<LedgerEntry>, Decision> decide, CancellationToken cancellation);

    /// <summary>
    ///     The same for a run of the queue (spec bench-run-queue «Результат прогона»): the line, the report and the end of the run
    ///     (<see cref="RunState.Finished" />) are one step, and the step checks that the Worker still holds the lease of the run.
    ///     A Worker that does not hold it writes nothing and <paramref name="decide" /> is not called.
    /// </summary>
    /// <exception cref="LeaseLostException">The Worker does not hold the lease of the run.</exception>
    Task<LedgerEntry> Decide(Func<IReadOnlyList<LedgerEntry>, Decision> decide, RunAssignment? assignment, CancellationToken cancellation);
}
