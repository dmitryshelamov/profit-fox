using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The setups of a pair on an interval on the last closed minute. The last candle is the window of the last minutes of the interval's
///     length, sliding every minute; an impulse is measured over the last <see cref="SetupThresholds.Candles" /> such lengths. Moves are in
///     ATR(14) of the closed candles before the last one, volumes in the median volume of the last 20 of them.
/// </summary>
public static class SetupDetector
{
    private const int MedianCandles = 20;

    /// <param name="closed">The closed candles of the interval before the last candle, oldest first.</param>
    /// <param name="minutes">The minutes up to the last closed one, oldest first, without gaps.</param>
    /// <param name="candleMinutes">The length of a candle of the interval in minutes.</param>
    /// <param name="thresholds">The thresholds of the interval.</param>
    /// <param name="volatilityPercentile">The 90th percentile of ATR / price of the pair on the interval; none, no high volatility.</param>
    /// <returns>None while the pair has fewer than 20 closed candles or its price stands still.</returns>
    public static SetupReading? Detect(
        IReadOnlyList<IdeaCandle> closed,
        IReadOnlyList<IdeaCandle> minutes,
        int candleMinutes,
        SetupThresholds thresholds,
        decimal? volatilityPercentile)
    {
        if (closed.Count < MedianCandles || Atr.Compute(closed) is not { } atr || atr == 0)
        {
            return null;
        }

        decimal median = Median([.. closed.TakeLast(MedianCandles).Select(candle => candle.Volume)]);
        Window impulse = Measure(minutes.TakeLast(thresholds.Candles * candleMinutes), thresholds.Candles, atr, median);
        Window last = Measure(minutes.TakeLast(candleMinutes), 1, atr, median);
        var signals = new List<SetupSignal>();
        bool rise = impulse.Move >= thresholds.Move && impulse.Volume >= thresholds.Volume;
        bool fall = -impulse.Move >= thresholds.Move && impulse.Volume >= thresholds.Volume;

        if (rise)
        {
            signals.Add(impulse.Signal(IdeaSetup.Impulse));
        }

        if (fall)
        {
            signals.Add(impulse.Signal(IdeaSetup.Drop));
        }

        if (!rise && last.Move >= thresholds.EarlyMove && last.Volume >= thresholds.EarlyVolume)
        {
            signals.Add(last.Signal(IdeaSetup.EarlyImpulse));
        }

        if (!fall && -last.Move >= thresholds.EarlyMove && last.Volume >= thresholds.EarlyVolume)
        {
            signals.Add(last.Signal(IdeaSetup.EarlyDrop));
        }

        if (atr / minutes[^1].Close > volatilityPercentile)
        {
            signals.Add(last.Signal(IdeaSetup.HighVolatility));
        }

        return new SetupReading(atr, signals);
    }

    /// <summary>The setups of <paramref name="now" /> that did not hold on the minute before: a setup fires once, as its condition starts to hold.</summary>
    public static IReadOnlyList<SetupSignal> Fired(IReadOnlyCollection<IdeaSetup> before, SetupReading? now)
    {
        return [.. now?.Signals.Where(signal => !before.Contains(signal.Setup)) ?? []];
    }

    private static decimal Median(decimal[] volumes)
    {
        Array.Sort(volumes);

        return (volumes[(volumes.Length / 2) - 1] + volumes[volumes.Length / 2]) / 2;
    }

    private static Window Measure(IEnumerable<IdeaCandle> minutes, int candles, decimal atr, decimal median)
    {
        IdeaCandle[] window = [.. minutes];
        decimal volume = window.Sum(minute => minute.Volume) / candles;

        return new Window(
            (window[^1].Close - window[0].Open) / atr,
            median > 0 ? volume / median : 0m,
            window.Min(minute => minute.Low),
            window.Max(minute => minute.High));
    }

    /// <summary>The move of a window in ATR, signed, and its volume a candle in medians.</summary>
    private sealed record Window(decimal Move, decimal Volume, decimal Low, decimal High)
    {
        public SetupSignal Signal(IdeaSetup setup)
        {
            return new SetupSignal(setup, Math.Abs(Move), Volume, Low, High);
        }
    }
}