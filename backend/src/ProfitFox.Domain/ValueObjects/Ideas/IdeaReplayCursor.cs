namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>How far the replay of a pair has gone, and the signals whose paths go on there; the next turn goes on from them.</summary>
public sealed record IdeaReplayCursor(DateTimeOffset ReplayedUntil, IReadOnlyList<IdeaSignal> Open);