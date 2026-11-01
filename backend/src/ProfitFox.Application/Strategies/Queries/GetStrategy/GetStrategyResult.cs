using ProfitFox.Domain.Entities.Strategies;

namespace ProfitFox.Application.Strategies.Queries.GetStrategy;

public sealed record GetStrategyResult(Strategy Strategy, int Version);