namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The thresholds of the setups on an interval: an impulse moves at least <see cref="Move" /> ATR over the last <see cref="Candles" />
///     candles with at least <see cref="Volume" /> medians of volume a candle; an early one moves at least <see cref="EarlyMove" /> ATR over the
///     last candle with at least <see cref="EarlyVolume" /> medians. The candle setups: a breakout closes <see cref="BreakoutMargin" /> (d) ATR
///     beyond a zone with <see cref="BreakoutVolume" /> (Vп) medians; a false breakout sweeps <see cref="SweepMargin" /> (e) ATR beyond it; a
///     squeeze ranges over the last <see cref="SqueezeCandles" /> (Nс) closed candles; an exhaustion has <see cref="ClimaxVolume" /> (Xк)
///     medians and a wick of <see cref="Wick" /> (w) bodies; a quiet volume has <see cref="QuietVolume" /> (Xт) medians and a body under
///     <see cref="QuietBody" /> (Bт) ATR; RSI leaves the zones beyond <see cref="Overbought" /> and <see cref="Oversold" />.
/// </summary>
public sealed record SetupThresholds(
    int Candles,
    decimal Move,
    decimal Volume,
    decimal EarlyMove,
    decimal EarlyVolume,
    decimal BreakoutMargin,
    decimal BreakoutVolume,
    decimal SweepMargin,
    int SqueezeCandles,
    decimal ClimaxVolume,
    decimal Wick,
    decimal QuietVolume,
    decimal QuietBody,
    decimal Overbought,
    decimal Oversold);