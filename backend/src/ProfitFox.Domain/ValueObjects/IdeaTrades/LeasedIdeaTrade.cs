using ProfitFox.Domain.Entities.IdeaTrades;

namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>A trade a Worker has taken; <see cref="Epoch" /> fences every write of this take.</summary>
public sealed record LeasedIdeaTrade(IdeaTrade Trade, long Epoch);