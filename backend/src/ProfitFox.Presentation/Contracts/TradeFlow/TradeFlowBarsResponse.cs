using ProfitFox.Application.MarketData;
using ProfitFox.Application.TradeFlow.Queries.GetTradeFlowBars;

namespace ProfitFox.Presentation.Contracts.TradeFlow;

/// <summary>
///     The bars of the trade flow of a pair, oldest first, with the first stored minute of the pair (<c>null</c> without one) and the gaps of the
///     flow in the period. Volumes and turnovers are decimal strings: JSON numbers lose precision in JavaScript clients.
/// </summary>
public sealed record TradeFlowBarsResponse(string Interval, DateTimeOffset? Since, IReadOnlyList<TradeFlowBarResponse> Bars, IReadOnlyList<TradeFlowGapResponse> Gaps)
{
    public static TradeFlowBarsResponse Of(GetTradeFlowBarsResult result)
    {
        return new TradeFlowBarsResponse(
            result.Interval.ToCode(),
            result.Since,
            [.. result.Bars!.Select(TradeFlowBarResponse.Of)],
            [.. result.Gaps.Select(gap => new TradeFlowGapResponse(gap.From, gap.To))]);
    }
}