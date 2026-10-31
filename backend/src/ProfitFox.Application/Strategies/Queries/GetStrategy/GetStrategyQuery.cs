using MediatR;

using ProfitFox.Domain.Entities.Strategies;

namespace ProfitFox.Application.Strategies.Queries.GetStrategy;

/// <summary>The strategy with this id, or <c>null</c> when there is none.</summary>
public sealed record GetStrategyQuery(Guid Id) : IRequest<Strategy?>;