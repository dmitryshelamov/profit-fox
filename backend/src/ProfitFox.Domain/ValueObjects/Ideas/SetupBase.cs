namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     What the setups of a pair on an interval are measured against while the window of the last candle slides (decision 2): the closed
///     candles before it, oldest first, their ATR(14), the median volume of the last 20, their zones and the smoothing of RSI after them.
///     It changes only as a candle closes.
/// </summary>
public sealed record SetupBase(IReadOnlyList<IdeaCandle> Closed, decimal Atr, decimal Median, IReadOnlyList<Zone> Zones, RsiState? Rsi);