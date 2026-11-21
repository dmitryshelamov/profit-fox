using ProfitFox.Domain.Ledger;

namespace ProfitFox.Application.Runs;

/// <summary>
///     The queue of runs (spec bench-run-queue): the runs that wait and go, taken by Workers with a lease. The end of a run is
///     not here: its line goes through <see cref="IRunLedger.Decide(Func{IReadOnlyList{LedgerEntry}, Decision}, RunAssignment?, CancellationToken)" />,
///     which ends the run in the same step. All times are the times of the database, so Workers on different machines agree.
/// </summary>
public interface IRunQueue
{
    /// <summary>
    ///     Puts a run of the version of a hypothesis on a period in the queue, waiting.
    /// </summary>
    /// <exception cref="LedgerException">The same version, period and kind already wait or go; <see cref="LedgerException.RunId" /> is that run.</exception>
    Task<RunRecord> Enqueue(string id, string hypothesis, int version, string period, bool parity, CancellationToken cancellation);

    /// <summary>
    ///     Takes the run that was queued first: a waiting one, or a going one whose lease has expired. The run is leased to
    ///     <paramref name="worker" /> for <paramref name="lease" />; its progress starts from zero, and when the run was going
    ///     its <see cref="RunRecord.LostLeases" /> grows by one. Null when there is nothing to take. Two Workers never take
    ///     the same run.
    /// </summary>
    Task<RunRecord?> Claim(string worker, TimeSpan lease, CancellationToken cancellation);

    /// <summary>
    ///     Extends the lease of a run by <paramref name="lease" /> from now and records its progress. Null when
    ///     <paramref name="worker" /> does not hold the run any more.
    /// </summary>
    Task<Renewal?> Renew(string runId, string worker, TimeSpan lease, int pairsDone, int pairsTotal, CancellationToken cancellation);

    /// <summary>
    ///     Takes a waiting run out of the queue, or asks the Worker of a going one to stop.
    /// </summary>
    Task<CancelOutcome> Cancel(string runId, CancellationToken cancellation);

    /// <summary>
    ///     The runs that wait and go, the one queued first first.
    /// </summary>
    Task<IReadOnlyList<RunRecord>> Active(CancellationToken cancellation);

    /// <summary>
    ///     The run <paramref name="runId" />, or null when the queue has no such run: it never had one, or the run was a waiting
    ///     one that was cancelled.
    /// </summary>
    Task<RunRecord?> Find(string runId, CancellationToken cancellation);
}
