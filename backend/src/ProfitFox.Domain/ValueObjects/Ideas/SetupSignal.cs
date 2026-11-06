using ProfitFox.Domain.Entities.Ideas.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     A setup that holds on the last closed minute, with what it was measured by: the side of its ideas (none for both), the move of its
///     window in ATR (up for an impulse, down for a drop), its volume a candle in medians, and the low and high of the window.
/// </summary>
public sealed record SetupSignal(IdeaSetup Setup, IdeaSide? Side, decimal Move, decimal VolumeRatio, decimal WindowLow, decimal WindowHigh)
{
    /// <summary>The sides of the ideas of the setup.</summary>
    public IReadOnlyList<IdeaSide> Sides => Side is { } side ? [side] : [IdeaSide.Long, IdeaSide.Short];
}