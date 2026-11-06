using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Domain.ValueObjects.IdeaAutos;

/// <summary>The check of a paper entry: what stops it, and the size it was given (none while the rules of the instrument are unknown).</summary>
public sealed record PaperEntryCheck(IReadOnlyList<AutoRefusal> Refusals, IdeaTradeSize? Size);