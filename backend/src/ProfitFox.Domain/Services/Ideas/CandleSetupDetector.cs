using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The candle setups of a pair on an interval on the last closed minute (decision 4), beside the setups of a move of
///     <see cref="SetupDetector" />. Their window is the last candle unless a setup says otherwise; moves are in ATR, volumes in medians.
/// </summary>
public static class CandleSetupDetector
{
    private const int ExtremeCandles = 200;

    public static IReadOnlyList<SetupSignal> Detect(CandleSetupInput input, SetupThresholds thresholds)
    {
        var signals = new List<SetupSignal>();
        IdeaCandle last = input.Last;
        decimal volume = input.Median > 0 ? last.Volume / input.Median : 0m;
        decimal margin = thresholds.BreakoutMargin * input.Atr;

        // «Пробой»: the candle opened at the zone and the last minute closed beyond it by the margin, with volume.
        if (volume >= thresholds.BreakoutVolume)
        {
            Zone? up = input.Zones.Where(zone => last.Open <= zone.High && last.Close >= zone.High + margin).MaxBy(zone => zone.High);
            Zone? down = input.Zones.Where(zone => last.Open >= zone.Low && last.Close <= zone.Low - margin).MinBy(zone => zone.Low);

            if (up is not null)
            {
                signals.Add(Signal(IdeaSetup.Breakout, IdeaSide.Long, input, volume, up));
            }

            if (down is not null)
            {
                signals.Add(Signal(IdeaSetup.Breakout, IdeaSide.Short, input, volume, down));
            }
        }

        // «Ложный пробой»: the candle opened inside the near edge, swept beyond it by the margin and the last minute closed back; against the sweep.
        decimal sweep = thresholds.SweepMargin * input.Atr;
        Zone? support = input.Zones.Where(zone => last.Open >= zone.Low && last.Low <= zone.Low - sweep && last.Close > zone.Low).MaxBy(zone => zone.Low);
        Zone? resistance = input.Zones.Where(zone => last.Open <= zone.High && last.High >= zone.High + sweep && last.Close < zone.High).MinBy(zone => zone.High);

        if (support is not null)
        {
            signals.Add(Signal(IdeaSetup.FalseBreakout, IdeaSide.Long, input, volume, support));
        }

        if (resistance is not null)
        {
            signals.Add(Signal(IdeaSetup.FalseBreakout, IdeaSide.Short, input, volume, resistance));
        }

        // «Сжатие»: ATR / price below the 10th percentile; its range is the last Nс closed candles and the last one.
        if (input.Atr / last.Close < input.LowPercentile)
        {
            IdeaCandle[] range = [.. input.Closed.TakeLast(thresholds.SqueezeCandles), last];
            signals.Add(Signal(IdeaSetup.Squeeze, null, input, volume) with { WindowLow = range.Min(candle => candle.Low), WindowHigh = range.Max(candle => candle.High) });
        }

        // «Истощение»: an impulse or a drop with a climax of volume and a wick against it of w bodies; against the move, in its window.
        if (volume >= thresholds.ClimaxVolume)
        {
            decimal body = Math.Abs(last.Close - last.Open);
            decimal upper = last.High - Math.Max(last.Open, last.Close);
            decimal lower = Math.Min(last.Open, last.Close) - last.Low;

            foreach (SetupSignal move in input.Moves)
            {
                decimal wick = move.Setup == IdeaSetup.Impulse ? upper : move.Setup == IdeaSetup.Drop ? lower : 0m;

                if (wick > 0 && wick >= thresholds.Wick * body)
                {
                    signals.Add(move with { Setup = IdeaSetup.Exhaustion, Side = move.Side == IdeaSide.Long ? IdeaSide.Short : IdeaSide.Long });
                }
            }
        }

        // «Перекупленность / перепроданность»: RSI of the closed candles beyond the bound, with the last candle as the next one back inside it;
        // against the zone, in the window of the last closed candle and the last one.
        if (input.Rsi is { } rsi)
        {
            decimal? closed = rsi.Value;
            decimal? now = rsi.With(last.Close);
            IdeaSide? side = closed >= thresholds.Overbought && now < thresholds.Overbought ? IdeaSide.Short
                : closed <= thresholds.Oversold && now > thresholds.Oversold ? IdeaSide.Long
                : null;

            if (side is not null)
            {
                IdeaCandle before = input.Closed[^1];
                signals.Add(Signal(IdeaSetup.RsiExtreme, side, input, volume) with { WindowLow = Math.Min(before.Low, last.Low), WindowHigh = Math.Max(before.High, last.High) });
            }
        }

        // «Новый максимум / минимум»: the last minute closed beyond the high or the low of the last 200 closed candles; the side of the way out.
        if (input.Closed.Count >= ExtremeCandles)
        {
            IdeaCandle[] candles = [.. input.Closed.TakeLast(ExtremeCandles)];

            if (last.Close > candles.Max(candle => candle.High))
            {
                signals.Add(Signal(IdeaSetup.NewExtreme, IdeaSide.Long, input, volume));
            }

            if (last.Close < candles.Min(candle => candle.Low))
            {
                signals.Add(Signal(IdeaSetup.NewExtreme, IdeaSide.Short, input, volume));
            }
        }

        // «Объём без движения»: Xт medians of volume with a body under Bт ATR; both sides, the range of the last candle.
        if (volume >= thresholds.QuietVolume && Math.Abs(last.Close - last.Open) < thresholds.QuietBody * input.Atr)
        {
            signals.Add(Signal(IdeaSetup.QuietVolume, null, input, volume));
        }

        return signals;
    }

    private static SetupSignal Signal(IdeaSetup setup, IdeaSide? side, CandleSetupInput input, decimal volume, Zone? zone = null)
    {
        IdeaCandle last = input.Last;

        return new SetupSignal(setup, side, Math.Abs(last.Close - last.Open) / input.Atr, volume, last.Low, last.High, zone);
    }
}