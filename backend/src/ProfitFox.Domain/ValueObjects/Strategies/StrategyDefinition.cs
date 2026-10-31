using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

public sealed record StrategyDefinition(StrategyDirection Direction, IReadOnlyList<StrategyParameter> Parameters, Graph Graph);