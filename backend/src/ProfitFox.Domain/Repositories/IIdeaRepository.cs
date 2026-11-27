using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.Lists;

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
    ///     Writes what changed in every idea of <paramref name="ideas" /> in one statement (task 18.6): the scanner writes the
    ///     open ideas it walked once a pass.
    /// </summary>
    Task SaveAllAsync(IReadOnlyCollection<Idea> ideas, CancellationToken cancellationToken);

    /// <summary>
    ///     Every open idea.
    /// </summary>
    Task<IReadOnlyList<Idea>> OpenAsync(CancellationToken cancellationToken);

    Task<Idea?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    ///     A page of the ideas that match the query, sorted by a field of <see cref="Services.Lists.ListFields.Ideas" />, a
    ///     missing value last.
    /// </summary>
    Task<ListPage<Idea>> ListAsync(IdeaListQuery query, CancellationToken cancellationToken);

    /// <summary>
    ///     A page of the summary of the closed ideas that match the query, by strategy, version and timeframe, sorted by a
    ///     field of <see cref="Services.Lists.ListFields.Summary" />; excluded ideas do not count.
    /// </summary>
    Task<ListPage<IdeaSummary>> SummaryAsync(IdeaListQuery query, CancellationToken cancellationToken);

    /// <summary>
    ///     How many ideas that match the query each strategy has, by its name, in no particular order; the strategy of the
    ///     query is not a condition (spec strategy-ideas «Число идей по стратегиям»). A strategy without such ideas has no
    ///     row.
    /// </summary>
    Task<IReadOnlyList<IdeaStrategyCount>> StrategyCountsAsync(IdeaListQuery query, CancellationToken cancellationToken);

    /// <summary>
    ///     The closed ideas that match the query by their score now (change add-idea-score, spec idea-score «Итоги по баллу»):
    ///     a row a score with at least one idea, from the highest score; excluded ideas do not count.
    /// </summary>
    Task<IReadOnlyList<IdeaScoreRow>> ScoresAsync(IdeaListQuery query, CancellationToken cancellationToken);

    /// <summary>
    ///     How many open and how many closed ideas there are.
    /// </summary>
    Task<(int Open, int Closed)> CountsAsync(CancellationToken cancellationToken);
}
