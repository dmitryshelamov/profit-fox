using ProfitFox.Domain.ValueObjects.Lists.Enums;

namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     What a list of ideas is asked for (spec strategy-ideas «Список идей», spec market-overview «Идеи пары»): open or
///     closed ideas, or every idea of the pair <see cref="Symbol" />; the page; a part of the symbol, the strategy, the
///     timeframe, the side and the label; for open ones only those in the plus or with a stop, for closed ones the reason of
///     the exit and the days back the exit is in; for those of a pair their state; and the conditions of a saved filter.
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
    IReadOnlyList<FilterCondition>? Conditions = null);
