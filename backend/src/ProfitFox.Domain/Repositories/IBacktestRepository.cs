using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;

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

    /// <summary>Every <c>Pending</c>, <c>LoadingCandles</c> and <c>Running</c> backtest, oldest first.</summary>
    Task<IReadOnlyList<Backtest>> GetUnfinishedAsync(CancellationToken cancellationToken);

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

    /// <summary>
    ///     Saves the progress of a running backtest with new results in one transaction; a result already stored stays as it was. Nothing is
    ///     written when the stored backtest is no longer running.
    /// </summary>
    Task<bool> SaveProgressAsync(Backtest backtest, IReadOnlyList<BacktestResult> results, CancellationToken cancellationToken);

    /// <summary>Numbers of the combinations that have a result, ascending.</summary>
    Task<IReadOnlyList<long>> GetCalculatedCombinationsAsync(Guid backtestId, CancellationToken cancellationToken);

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