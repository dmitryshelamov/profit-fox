using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Stores backtests and their results. A write that expects a status changes nothing and returns <c>false</c> when the stored
///     backtest is no longer in it: a cancel or a deletion of the bot happened meanwhile.
/// </summary>
public interface IBacktestRepository
{
    Task AddAsync(Backtest backtest, CancellationToken cancellationToken);

    Task<Backtest?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Backtests of the bot, newest first.</summary>
    Task<IReadOnlyList<Backtest>> GetByBotAsync(Guid botId, CancellationToken cancellationToken);

    /// <summary>Every <c>Pending</c>, <c>LoadingCandles</c> and <c>Running</c> backtest of <paramref name="engineVersion" />, oldest first.</summary>
    Task<IReadOnlyList<Backtest>> GetUnfinishedAsync(int engineVersion, CancellationToken cancellationToken);

    /// <summary>Fails with <paramref name="reason" /> every unfinished backtest of an engine version older than <paramref name="engineVersion" />.</summary>
    Task FailOutdatedAsync(int engineVersion, string reason, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Saves the backtest if the stored one is still in <paramref name="expected" />.</summary>
    Task<bool> UpdateAsync(Backtest backtest, BacktestStatus expected, CancellationToken cancellationToken);

    /// <summary>
    ///     Moves the backtest to <paramref name="status" />, finished at <paramref name="finishedAt" />, if the stored one is in one of
    ///     <paramref name="expected" />: the check and the write are one statement, so a run that moves the backtest meanwhile never
    ///     loses the change. Returns <c>false</c> when there is no such backtest or it is in another status.
    /// </summary>
    Task<bool> UpdateStatusAsync(
        Guid id,
        IReadOnlyCollection<BacktestStatus> expected,
        BacktestStatus status,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken);

    /// <summary>Fails the backtest with <paramref name="reason" /> if the stored one is in one of <paramref name="expected" />.</summary>
    Task<bool> FailAsync(Guid id, IReadOnlyCollection<BacktestStatus> expected, string reason, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    ///     Adds <paramref name="job" /> and saves the loading backtest that waits for it in one transaction, if the stored backtest is still
    ///     loading and waits for no job yet; otherwise writes nothing and returns <c>false</c>.
    /// </summary>
    Task<bool> StartImportAsync(Backtest backtest, CandleImportJob job, CancellationToken cancellationToken);

    /// <summary>Saves the running backtest and adds its chunks in one transaction, if the stored backtest is still loading.</summary>
    Task<bool> StartRunningAsync(Backtest backtest, IReadOnlyList<BacktestChunk> chunks, CancellationToken cancellationToken);

    /// <summary>
    ///     Leases to <paramref name="owner" /> until <paramref name="leasedUntil" /> the first chunk of the running backtest that is not done
    ///     and has no live lease at <paramref name="now" />. Taking over an expired lease counts one more lost lease of the chunk. Returns
    ///     the chunk as leased, or <c>null</c>.
    /// </summary>
    Task<BacktestChunk?> ClaimChunkAsync(Guid backtestId, Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken);

    /// <summary>Extends the lease of <paramref name="owner" /> on the chunk; <c>false</c> when the owner no longer holds it.</summary>
    Task<bool> RenewChunkLeaseAsync(BacktestChunk chunk, Guid owner, DateTimeOffset leasedUntil, CancellationToken cancellationToken);

    /// <summary>
    ///     Saves new results of the chunk in one transaction: a result already stored stays as it was, the backtest counts the new ones, the
    ///     chunk takes <paramref name="speed" /> and its lost leases end, and the backtest's speed becomes the sum over its chunks with a
    ///     live lease at <paramref name="now" />. Writes nothing and returns <c>false</c> when the backtest is no longer running or
    ///     <paramref name="owner" /> lost the chunk.
    /// </summary>
    Task<bool> SaveChunkProgressAsync(
        BacktestChunk chunk,
        Guid owner,
        IReadOnlyList<BacktestResult> results,
        decimal? speed,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Marks the chunk done and ends its lease if <paramref name="owner" /> still holds it.</summary>
    Task<bool> CompleteChunkAsync(BacktestChunk chunk, Guid owner, CancellationToken cancellationToken);

    /// <summary>Moves the running backtest to <c>Succeeded</c> if all its chunks are done; <c>false</c> otherwise.</summary>
    Task<bool> SucceedIfCompleteAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Numbers of the combinations that have a result, ascending.</summary>
    Task<IReadOnlyList<long>> GetCalculatedCombinationsAsync(Guid backtestId, CancellationToken cancellationToken);

    /// <summary>Numbers of the combinations of the chunk that have a result, ascending.</summary>
    Task<IReadOnlyList<long>> GetCalculatedCombinationsAsync(BacktestChunk chunk, CancellationToken cancellationToken);

    /// <summary>A page of results sorted by <paramref name="sort" />, a missing value last, equal values by combination.</summary>
    Task<IReadOnlyList<BacktestResult>> GetResultsAsync(
        Guid backtestId,
        ResultMetric sort,
        bool descending,
        int offset,
        int limit,
        CancellationToken cancellationToken);

    Task<long> CountResultsAsync(Guid backtestId, CancellationToken cancellationToken);

    Task<BacktestResult?> GetResultAsync(Guid backtestId, long combination, CancellationToken cancellationToken);
}