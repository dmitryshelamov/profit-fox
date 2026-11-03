using ProfitFox.Application.Ports.MarketData;

namespace ProfitFox.Application.MarketData.FormingCandles;

/// <summary>The state of a feed and the newest candle it took, <c>null</c> before the first update.</summary>
public sealed record FormingSnapshot(FormingState State, FormingCandle? Candle);