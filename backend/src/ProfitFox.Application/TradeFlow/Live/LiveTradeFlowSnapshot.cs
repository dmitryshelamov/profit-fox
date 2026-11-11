using ProfitFox.Application.MarketData.FormingCandles;

namespace ProfitFox.Application.TradeFlow.Live;

/// <summary>The state of the feed of a pair and the newest sums it took, <c>null</c> before the first trade.</summary>
public sealed record LiveTradeFlowSnapshot(FormingState State, LiveTradeFlow? Flow);