using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Runs.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     The backtests of the bench (spec bench-run-queue; change add-strategy-backtest, design.md, decision 2): the queue
///     taken by Workers with a lease, and the results. A backtest does not wait for any other. All times are the times of
///     the database, so Workers on different machines agree.
/// </summary>
public interface IBacktestRepository
{
    /// <summary>
    ///     Puts a backtest with its sets in the queue, waiting.
    /// </summary>
    Task<Backtest> EnqueueAsync(Backtest backtest, CancellationToken cancellationToken);

    /// <summary>
    ///     Takes the backtest that was queued first: a waiting one, or a going one whose lease has expired. It is leased to
    ///     <paramref name="worker" /> for <paramref name="lease" />; its progress starts from zero, and when it was going its
    ///     <see cref="Backtest.LostLeases" /> grows by one. Null when there is nothing to take. Two Workers never take the
    ///     same backtest.
    /// </summary>
    Task<Backtest?> ClaimAsync(string worker, TimeSpan lease, CancellationToken cancellationToken);

    /// <summary>
    ///     Extends the lease by <paramref name="lease" /> from now and records the progress. Null when
    ///     <paramref name="worker" /> does not hold the backtest any more.
    /// </summary>
    Task<Renewal?> RenewAsync(string id, string worker, TimeSpan lease, int pairsDone, int pairsTotal, CancellationToken cancellationToken);

    /// <summary>
    ///     Takes a waiting backtest out of the queue, or asks the Worker of a going one to stop.
    /// </summary>
    Task<CancelOutcome> CancelAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    ///     Ends the backtest of <paramref name="assignment" /> as done with its numbers, in one step.
    /// </summary>
    /// <exception cref="LeaseLostException">The Worker does not hold the backtest; nothing is written.</exception>
    Task FinishAsync(RunAssignment assignment, BacktestOutcome outcome, CancellationToken cancellationToken);

    /// <summary>
    ///     Ends the backtest of <paramref name="assignment" /> without numbers: <see cref="BacktestState.Failed" /> or
    ///     <see cref="BacktestState.Cancelled" /> with the reason.
    /// </summary>
    /// <exception cref="LeaseLostException">The Worker does not hold the backtest; nothing is written.</exception>
    Task StopAsync(RunAssignment assignment, string status, string reason, CancellationToken cancellationToken);

    /// <summary>
    ///     The backtests that wait and go, the one queued first first.
    /// </summary>
    Task<IReadOnlyList<Backtest>> ActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     The latest backtests that ended, the newest first, at most <paramref name="limit" />, with the sum of their best set.
    /// </summary>
    Task<IReadOnlyList<Backtest>> FinishedAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    ///     The backtest with its sets, or null when there is none: it never was, or it was cancelled while waiting.
    /// </summary>
    Task<Backtest?> FindAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    ///     The pairs of a set of a done backtest by the sum of their results from the highest; null when the backtest has
    ///     no such set.
    /// </summary>
    Task<IReadOnlyList<PairSummary>?> PairsAsync(string id, int set, CancellationToken cancellationToken);

    /// <summary>
    ///     The last backtest of each strategy by its id (spec strategy-constructor «Последний прогон стратегии»): the first
    ///     in the queue of those that wait or go, otherwise the one that ended last.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Backtest>> LastOfStrategiesAsync(CancellationToken cancellationToken);
}
