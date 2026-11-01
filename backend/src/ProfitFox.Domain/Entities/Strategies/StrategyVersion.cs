using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Entities.Strategies;

/// <summary>An immutable numbered snapshot of a strategy; numbers start at 1 and grow with every change that affects execution.</summary>
public sealed record StrategyVersion(Guid StrategyId, int Number, StrategySnapshot Snapshot, DateTimeOffset CreatedAt);