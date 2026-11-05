using ProfitFox.Domain.Entities.Ideas;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>How far the replay of a pair has gone, and the ideas of the replay still open there; the next turn goes on from them.</summary>
public sealed record IdeaReplayCursor(DateTimeOffset ReplayedUntil, IReadOnlyList<Idea> Open);