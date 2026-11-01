using MediatR;

namespace ProfitFox.Application.Strategies.Queries.GetStrategy;

/// <summary>The strategy with this id and the number of its latest version, or <c>null</c> when there is none.</summary>
public sealed record GetStrategyQuery(Guid Id) : IRequest<GetStrategyResult?>;