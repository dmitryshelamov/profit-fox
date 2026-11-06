using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>N, A, V, a and v of the setups of a move (decision 7), d, Vп, e, Nс, Xк, w, Xт, Bт and the bounds of RSI of the candle setups.</summary>
public sealed class ThresholdSettings
{
    public int Candles { get; init; } = 6;

    public decimal Move { get; init; } = 3.0m;

    public decimal Volume { get; init; } = 2.0m;

    public decimal EarlyMove { get; init; } = 1.5m;

    public decimal EarlyVolume { get; init; } = 3.0m;

    public decimal BreakoutMargin { get; init; }

    public decimal BreakoutVolume { get; init; }

    public decimal SweepMargin { get; init; }

    public int SqueezeCandles { get; init; }

    public decimal ClimaxVolume { get; init; }

    public decimal Wick { get; init; }

    public decimal QuietVolume { get; init; }

    public decimal QuietBody { get; init; }

    public decimal Overbought { get; init; }

    public decimal Oversold { get; init; }

    /// <summary>These thresholds with the values an interval sets of its own.</summary>
    public SetupThresholds With(ThresholdOverride? own)
    {
        return new SetupThresholds(
            own?.Candles ?? Candles,
            own?.Move ?? Move,
            own?.Volume ?? Volume,
            own?.EarlyMove ?? EarlyMove,
            own?.EarlyVolume ?? EarlyVolume,
            own?.BreakoutMargin ?? BreakoutMargin,
            own?.BreakoutVolume ?? BreakoutVolume,
            own?.SweepMargin ?? SweepMargin,
            own?.SqueezeCandles ?? SqueezeCandles,
            own?.ClimaxVolume ?? ClimaxVolume,
            own?.Wick ?? Wick,
            own?.QuietVolume ?? QuietVolume,
            own?.QuietBody ?? QuietBody,
            own?.Overbought ?? Overbought,
            own?.Oversold ?? Oversold);
    }
}