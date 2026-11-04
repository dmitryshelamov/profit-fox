using ProfitFox.Domain.Entities.Ideas;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>An idea of the replay of history that entered and closed, with its result in R after fees.</summary>
public sealed record IdeaCase(Idea Idea, decimal Result);