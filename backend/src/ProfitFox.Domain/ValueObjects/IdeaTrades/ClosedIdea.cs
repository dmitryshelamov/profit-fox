namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>An idea that closed and how it ended, to be written on the trades made of it.</summary>
public sealed record ClosedIdea(Guid IdeaId, TradedIdeaOutcome Outcome);