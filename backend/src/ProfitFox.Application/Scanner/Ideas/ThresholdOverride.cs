namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>The thresholds an interval sets of its own; a value not set is the common one.</summary>
public sealed class ThresholdOverride
{
    public int? Candles { get; init; }

    public decimal? Move { get; init; }

    public decimal? Volume { get; init; }

    public decimal? EarlyMove { get; init; }

    public decimal? EarlyVolume { get; init; }

    public decimal? BreakoutMargin { get; init; }

    public decimal? BreakoutVolume { get; init; }

    public decimal? SweepMargin { get; init; }

    public int? SqueezeCandles { get; init; }

    public decimal? ClimaxVolume { get; init; }

    public decimal? Wick { get; init; }

    public decimal? QuietVolume { get; init; }

    public decimal? QuietBody { get; init; }

    public decimal? BiasRatio { get; init; }

    public decimal? FlatMove { get; init; }

    public decimal? Overbought { get; init; }

    public decimal? Oversold { get; init; }
}