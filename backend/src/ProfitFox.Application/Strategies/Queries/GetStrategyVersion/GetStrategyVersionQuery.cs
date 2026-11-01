using MediatR;

using ProfitFox.Domain.Entities.Strategies;

namespace ProfitFox.Application.Strategies.Queries.GetStrategyVersion;

/// <summary>The version of the strategy with this number, or <c>null</c> when there is none.</summary>
public sealed record GetStrategyVersionQuery(Guid StrategyId, int Number) : IRequest<StrategyVersion?>;