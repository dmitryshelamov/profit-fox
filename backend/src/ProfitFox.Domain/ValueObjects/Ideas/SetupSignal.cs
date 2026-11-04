using ProfitFox.Domain.Entities.Ideas.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     A setup that holds on the last closed minute, with what it was measured by: the move of its window in ATR (up for an impulse, down for
///     a drop), its volume a candle in medians, and the low and high of the window.
/// </summary>
public sealed record SetupSignal(IdeaSetup Setup, decimal Move, decimal VolumeRatio, decimal WindowLow, decimal WindowHigh);