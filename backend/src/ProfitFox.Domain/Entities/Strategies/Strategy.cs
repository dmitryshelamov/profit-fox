using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Entities.Strategies;

public sealed record Strategy(Guid Id, string Name, StrategyDefinition Definition, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);