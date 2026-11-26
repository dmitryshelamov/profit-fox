using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The history of a pair as one pass of a backtest reads it: the candles of each timeframe of the sets from the start of
///     the data, the funding with the payment at the end, and the reader of the minutes. The first pass of a category of ideas
///     reads no minutes.
/// </summary>
public sealed record PairHistory(
    IReadOnlyDictionary<TimeSpan, IReadOnlyList<HistoryCandle>> Candles,
    HistoryRead<HistoryPayment> Funding,
    MinuteReader Minutes);