using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The setups of a pair on an interval on the last closed minute: the setups of a move here, the candle setups of
///     <see cref="CandleSetupDetector" /> beside them. The last candle is the window of the last minutes of the interval's length, sliding
///     every minute; an impulse is measured over the last <see cref="SetupThresholds.Candles" /> such lengths. Moves are in ATR(14) of the
///     closed candles before the last one, volumes in the median volume of the last 20 of them.
/// </summary>
public static class SetupDetector
{
    private const int MedianCandles = 20;

    /// <summary>What the setups are measured against after <paramref name="closed" />, oldest first, with the zones by <paramref name="rules" />.</summary>
    /// <returns>None while the pair has fewer than 20 closed candles or its price stands still: then it has no setups.</returns>
    public static SetupBase? Base(IReadOnlyList<IdeaCandle> closed, ZoneRules rules)
    {
        if (closed.Count < MedianCandles || Atr.Compute(closed) is not { } atr || atr == 0)
        {
            return null;
        }

        decimal median = Median([.. closed.TakeLast(MedianCandles).Select(candle => candle.Volume)]);

        return new SetupBase(closed, atr, median, ZoneFinder.Find(closed, atr, rules), Rsi.Smooth(closed));
    }

    /// <param name="basis">What the setups are measured against: the closed candles of the interval before the last candle.</param>
    /// <param name="minutes">The minutes up to the last closed one, oldest first, without gaps.</param>
    /// <param name="candleMinutes">The length of a candle of the interval in minutes.</param>
    /// <param name="thresholds">The thresholds of the interval.</param>
    /// <param name="percentiles">The percentiles of ATR / price of the pair on the interval; without one, no squeeze or no high volatility.</param>
    public static SetupReading Detect(
        SetupBase basis,
        IReadOnlyList<IdeaCandle> minutes,
        int candleMinutes,
        SetupThresholds thresholds,
        VolatilityPercentiles percentiles)
    {
        decimal atr = basis.Atr;
        decimal median = basis.Median;
        Window impulse = Measure(minutes, Math.Min(minutes.Count, thresholds.Candles * candleMinutes), thresholds.Candles, atr, median);
        Window last = Measure(minutes, Math.Min(minutes.Count, candleMinutes), 1, atr, median);
        var signals = new List<SetupSignal>();
        bool rise = impulse.Move >= thresholds.Move && impulse.Volume >= thresholds.Volume;
        bool fall = -impulse.Move >= thresholds.Move && impulse.Volume >= thresholds.Volume;

        if (rise)
        {
            signals.Add(impulse.Signal(IdeaSetup.Impulse, IdeaSide.Long));
        }

        if (fall)
        {
            signals.Add(impulse.Signal(IdeaSetup.Drop, IdeaSide.Short));
        }

        if (!rise && last.Move >= thresholds.EarlyMove && last.Volume >= thresholds.EarlyVolume)
        {
            signals.Add(last.Signal(IdeaSetup.EarlyImpulse, IdeaSide.Long));
        }

        if (!fall && -last.Move >= thresholds.EarlyMove && last.Volume >= thresholds.EarlyVolume)
        {
            signals.Add(last.Signal(IdeaSetup.EarlyDrop, IdeaSide.Short));
        }

        if (atr / minutes[^1].Close > percentiles.High)
        {
            signals.Add(last.Signal(IdeaSetup.HighVolatility, null));
        }

        var candle = new IdeaCandle(last.First.OpenTime, last.First.Open, last.High, last.Low, minutes[^1].Close, last.Sum);
        var input = new CandleSetupInput(basis.Closed, candle, atr, median, basis.Zones, basis.Rsi, percentiles.Low, [.. signals]);

        return new SetupReading(atr, [.. signals, .. CandleSetupDetector.Detect(input, thresholds)]);
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

    /// <summary>
    ///     The window of the last <paramref name="count" /> minutes, read in place: the replay reads it every minute of history, so it copies
    ///     nothing and takes the sum, the low and the high in one pass, in the order of the minutes (decision 1 of speed-up-ideas-replay).
    /// </summary>
    private static Window Measure(IReadOnlyList<IdeaCandle> minutes, int count, int candles, decimal atr, decimal median)
    {
        int first = minutes.Count - count;
        IdeaCandle open = minutes[first];
        decimal sum = open.Volume;
        decimal low = open.Low;
        decimal high = open.High;

        for (int index = first + 1; index < minutes.Count; index++)
        {
            IdeaCandle minute = minutes[index];
            sum += minute.Volume;

            if (minute.Low < low)
            {
                low = minute.Low;
            }

            if (minute.High > high)
            {
                high = minute.High;
            }
        }

        decimal volume = sum / candles;

        return new Window((minutes[^1].Close - open.Open) / atr, median > 0 ? volume / median : 0m, low, high, open, sum);
    }

    /// <summary>The move of a window in ATR, signed, and its volume a candle in medians; its first minute and the sum of its volumes.</summary>
    private sealed record Window(decimal Move, decimal Volume, decimal Low, decimal High, IdeaCandle First, decimal Sum)
    {
        public SetupSignal Signal(IdeaSetup setup, IdeaSide? side)
        {
            return new SetupSignal(setup, side, Math.Abs(Move), Volume, Low, High);
        }
    }
}