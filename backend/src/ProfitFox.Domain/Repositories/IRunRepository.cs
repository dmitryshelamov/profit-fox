using ProfitFox.Domain.Entities.Runs;
using ProfitFox.Domain.Entities.Runs.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     The queue of runs (spec bench-run-queue): the runs that wait and go, taken by Workers with a lease. The end of a run is
///     not here: its line goes through <see cref="ILedgerRepository.DecideAsync(Func{IReadOnlyList{LedgerEntry}, Decision}, RunAssignment?, CancellationToken)" />,
///     which ends the run in the same step. All times are the times of the database, so Workers on different machines agree.
/// </summary>
public interface IRunRepository
{
    /// <summary>
    ///     Puts a run of the version of a hypothesis on a period in the queue, waiting.
    /// </summary>
    /// <exception cref="LedgerException">The same version, period and kind already wait or go; <see cref="LedgerException.RunId" /> is that run.</exception>
    Task<Run> EnqueueAsync(string id, string hypothesis, int version, string period, bool parity, CancellationToken cancellationToken);

    /// <summary>
    ///     Takes the run that was queued first: a waiting one, or a going one whose lease has expired. The run is leased to
    ///     <paramref name="worker" /> for <paramref name="lease" />; its progress starts from zero, and when the run was going
    ///     its <see cref="Run.LostLeases" /> grows by one. Null when there is nothing to take. Two Workers never take
    ///     the same run.
    /// </summary>
    Task<Run?> ClaimAsync(string worker, TimeSpan lease, CancellationToken cancellationToken);

    /// <summary>
    ///     Extends the lease of a run by <paramref name="lease" /> from now and records its progress. Null when
    ///     <paramref name="worker" /> does not hold the run any more.
    /// </summary>
    Task<Renewal?> RenewAsync(string runId, string worker, TimeSpan lease, int pairsDone, int pairsTotal, CancellationToken cancellationToken);

    /// <summary>
    ///     Takes a waiting run out of the queue, or asks the Worker of a going one to stop.
    /// </summary>
    Task<CancelOutcome> CancelAsync(string runId, CancellationToken cancellationToken);

    /// <summary>
    ///     The runs that wait and go, the one queued first first.
    /// </summary>
    Task<IReadOnlyList<Run>> ActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     The run <paramref name="runId" />, or null when the queue has no such run: it never had one, or the run was a waiting
    ///     one that was cancelled.
    /// </summary>
    Task<Run?> FindAsync(string runId, CancellationToken cancellationToken);
}
