using MediatR;

using ProfitFox.Domain.Entities.Strategies;

namespace ProfitFox.Application.Strategies.Queries.GetStrategies;

public sealed record GetStrategiesQuery : IRequest<IReadOnlyList<Strategy>>;