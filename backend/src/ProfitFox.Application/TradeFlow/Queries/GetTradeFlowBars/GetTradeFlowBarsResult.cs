using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Application.TradeFlow.Queries.GetTradeFlowBars;

/// <summary>
///     Either the bars, oldest first, with the first stored minute of the pair (none without one) and the gaps of the flow in the period, or
///     validation errors keyed by parameter name; <see cref="NotFound" /> for a category without a trade flow.
/// </summary>
public sealed record GetTradeFlowBarsResult(
    Interval Interval,
    IReadOnlyList<TradeFlowMinute>? Bars,
    DateTimeOffset? Since,
    IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> Gaps,
    bool NotFound,
    IReadOnlyDictionary<string, string> Errors);