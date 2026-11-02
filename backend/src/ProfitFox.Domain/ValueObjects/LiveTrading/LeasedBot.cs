using ProfitFox.Domain.Entities.LiveTrading;

namespace ProfitFox.Domain.ValueObjects.LiveTrading;

/// <summary>A bot a Worker has taken; <see cref="Epoch" /> fences every write of this take.</summary>
public sealed record LeasedBot(LiveBot Bot, long Epoch);