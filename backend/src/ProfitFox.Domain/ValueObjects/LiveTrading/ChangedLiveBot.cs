using ProfitFox.Domain.Entities.LiveTrading;

namespace ProfitFox.Domain.ValueObjects.LiveTrading;

/// <summary>A bot of a Worker as it is now, the epoch of the take that holds it and when it was written last.</summary>
public sealed record ChangedLiveBot(LiveBot Bot, long Epoch, DateTimeOffset UpdatedAt);