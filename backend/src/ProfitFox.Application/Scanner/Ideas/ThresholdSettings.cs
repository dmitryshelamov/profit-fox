using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>N, A, V, a and v of the setups (decision 7).</summary>
public sealed class ThresholdSettings
{
    public int Candles { get; init; } = 6;

    public decimal Move { get; init; } = 3.0m;

    public decimal Volume { get; init; } = 2.0m;

    public decimal EarlyMove { get; init; } = 1.5m;

    public decimal EarlyVolume { get; init; } = 3.0m;

    /// <summary>These thresholds with the values an interval sets of its own.</summary>
    public SetupThresholds With(ThresholdOverride? own)
    {
        return new SetupThresholds(
            own?.Candles ?? Candles,
            own?.Move ?? Move,
            own?.Volume ?? Volume,
            own?.EarlyMove ?? EarlyMove,
            own?.EarlyVolume ?? EarlyVolume);
    }
}