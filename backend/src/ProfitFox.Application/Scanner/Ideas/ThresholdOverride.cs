namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>The thresholds an interval sets of its own; a value not set is the common one.</summary>
public sealed class ThresholdOverride
{
    public int? Candles { get; init; }

    public decimal? Move { get; init; }

    public decimal? Volume { get; init; }

    public decimal? EarlyMove { get; init; }

    public decimal? EarlyVolume { get; init; }
}