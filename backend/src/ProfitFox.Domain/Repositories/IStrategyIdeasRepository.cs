using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Where it is kept whether each strategy gives ideas (change add-ideas-and-market, design.md, decision 6); the record goes
///     with its strategy.
/// </summary>
public interface IStrategyIdeasRepository
{
    Task<StrategyIdeas?> FindAsync(Guid strategyId, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes the record of the strategy, replacing the one it had.
    /// </summary>
    Task SetAsync(StrategyIdeas ideas, CancellationToken cancellationToken);

    /// <summary>
    ///     The record of every strategy that has one.
    /// </summary>
    Task<IReadOnlyList<StrategyIdeas>> AllAsync(CancellationToken cancellationToken);
}
