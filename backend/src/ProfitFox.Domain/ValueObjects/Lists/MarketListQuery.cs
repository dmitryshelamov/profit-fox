namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     What the list of pairs of the market is asked for (spec market-overview «Список пар рынка»): the page, a part of the
///     symbol, only pairs with an open idea, the strategy and the timeframe of an open idea, and the conditions of a saved
///     filter.
/// </summary>
public sealed record MarketListQuery(
    ListOrder Order,
    string? Search = null,
    bool IdeasOnly = false,
    string? Strategy = null,
    string? Timeframe = null,
    IReadOnlyList<FilterCondition>? Conditions = null);
