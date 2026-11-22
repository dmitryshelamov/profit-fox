using ProfitFox.Domain.Entities.Ideas;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Where ideas are kept (change add-ideas-and-market, design.md, decisions 5 and 14): an idea is written at its birth,
///     moved on as its cycle is counted, and closed or excluded; closed and excluded ideas are kept for good.
/// </summary>
public interface IIdeaRepository
{
    /// <summary>
    ///     Writes a new idea; false when the idea of the strategy on the pair, the timeframe and the candle is written already,
    ///     so an idea is written once. That a strategy has one open idea on a pair and timeframe is the rule of the scanner
    ///     (spec strategy-ideas «Одна открытая идея»): an idea whose exit is at the close of a candle does not stop the order
    ///     of that candle.
    /// </summary>
    Task<bool> AddAsync(Idea idea, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes what changed in the idea: its entry, its result now, how far it is counted, and its state with the exit and
    ///     the outcome.
    /// </summary>
    Task SaveAsync(Idea idea, CancellationToken cancellationToken);

    /// <summary>
    ///     Every open idea.
    /// </summary>
    Task<IReadOnlyList<Idea>> OpenAsync(CancellationToken cancellationToken);

    Task<Idea?> FindAsync(Guid id, CancellationToken cancellationToken);
}
