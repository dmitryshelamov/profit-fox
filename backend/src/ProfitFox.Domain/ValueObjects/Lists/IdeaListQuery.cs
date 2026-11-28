using ProfitFox.Domain.ValueObjects.Lists.Enums;
using ProfitFox.Domain.ValueObjects.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     What a list of ideas is asked for (spec strategy-ideas «Список идей», spec market-overview «Идеи пары»): open or
///     closed ideas, or every idea of the pair <see cref="Symbol" />; the page; a part of the symbol, the strategy, the
///     timeframe, the side and the label; for open ones only those in the plus or with a stop, for closed ones the reason of
///     the exit and the days back the exit is in; for those of a pair their state; and the conditions of a saved filter.
///     For the chart of a pair (task 18.22): the window of time the ideas touch, from <see cref="From" /> to
///     <see cref="To" />, and the idea whose page is wanted, <see cref="Around" />, instead of the page of the order.
///     <see cref="PairKind" />: the kind of the pair of the idea in the snapshot of the market, every idea when null; an idea
///     of a pair out of the snapshot is of no kind (change add-instrument-kind-filter, decision 3). <see cref="MinScore" />:
///     only ideas with a score of at least it (change add-idea-score, spec idea-score «Балл в списках идей»).
/// </summary>
public sealed record IdeaListQuery(
    IdeaListKind Kind,
    ListOrder Order,
    string? Symbol = null,
    string? Search = null,
    string? Strategy = null,
    string? Timeframe = null,
    string? Side = null,
    string? Label = null,
    bool OnlyPositive = false,
    bool OnlyWithStop = false,
    string? ExitReason = null,
    int? PeriodDays = null,
    string? State = null,
    IReadOnlyList<FilterCondition>? Conditions = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? Around = null,
    InstrumentKind? PairKind = null,
    decimal? MinScore = null);
