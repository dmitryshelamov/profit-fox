namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The thresholds of the setups on an interval: an impulse moves at least <see cref="Move" /> ATR over the last <see cref="Candles" />
///     candles with at least <see cref="Volume" /> medians of volume a candle; an early one moves at least <see cref="EarlyMove" /> ATR over the
///     last candle with at least <see cref="EarlyVolume" /> medians.
/// </summary>
public sealed record SetupThresholds(int Candles, decimal Move, decimal Volume, decimal EarlyMove, decimal EarlyVolume);