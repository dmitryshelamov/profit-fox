namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     What the candle setups of a pair on an interval are read by on the last closed minute: the closed candles of the interval before the
///     last candle, the last candle (the window of the last minutes of the interval's length), ATR(14) and the median volume of the closed
///     candles, their zones, the smoothing of RSI after them, the 10th percentile of ATR / price of the pair and the setups of a move that hold.
/// </summary>
public sealed record CandleSetupInput(
    IReadOnlyList<IdeaCandle> Closed,
    IdeaCandle Last,
    decimal Atr,
    decimal Median,
    IReadOnlyList<Zone> Zones,
    RsiState? Rsi,
    decimal? LowPercentile,
    IReadOnlyList<SetupSignal> Moves);