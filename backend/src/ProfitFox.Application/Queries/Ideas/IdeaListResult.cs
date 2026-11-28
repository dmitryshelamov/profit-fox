using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.ValueObjects.Lists;

namespace ProfitFox.Application.Queries.Ideas;

/// <summary>
///     A page of ideas, and whether the saved filter asked for is gone; with the trade of each idea of the page that has one —
///     its latest not cancelled (change take-idea-trades, design.md, decision 13).
/// </summary>
public sealed record IdeaListResult(ListPage<Idea> Page, bool FilterGone)
{
    public IReadOnlyDictionary<Guid, (Guid TradeId, TradeState State)> Trades { get; init; } = new Dictionary<Guid, (Guid, TradeState)>();
}

/// <summary>
///     The outcomes by score, and whether the saved filter asked for is gone.
/// </summary>
public sealed record IdeaScoresResult(IReadOnlyList<IdeaScoreRow> Rows, bool FilterGone);

/// <summary>
///     A page of the summary of the journal, and whether the saved filter asked for is gone.
/// </summary>
public sealed record IdeaSummaryResult(ListPage<IdeaSummary> Page, bool FilterGone);
