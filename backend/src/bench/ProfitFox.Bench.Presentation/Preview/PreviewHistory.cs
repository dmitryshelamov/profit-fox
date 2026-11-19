using ProfitFox.Bench.MarketData.History;

namespace ProfitFox.Bench.Presentation.Preview;

/// <summary>
///     The history of one pair and timeframe over exploration that a preview reads: the candles with their coverage and
///     the funding payments.
/// </summary>
public sealed record PreviewHistory(HistoryRead<HistoryCandle> Candles, HistoryRead<HistoryPayment> Funding);
