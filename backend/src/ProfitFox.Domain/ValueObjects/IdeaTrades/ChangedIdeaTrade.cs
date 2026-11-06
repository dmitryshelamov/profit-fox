using ProfitFox.Domain.Entities.IdeaTrades;

namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>A trade of a Worker as it is now, the epoch of the take that holds it and when it was written last.</summary>
public sealed record ChangedIdeaTrade(IdeaTrade Trade, long Epoch, DateTimeOffset UpdatedAt);