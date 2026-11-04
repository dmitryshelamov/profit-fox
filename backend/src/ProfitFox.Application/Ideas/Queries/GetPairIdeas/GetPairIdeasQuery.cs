using MediatR;

namespace ProfitFox.Application.Ideas.Queries.GetPairIdeas;

/// <summary>The open ideas of a pair and the ones closed in the last 24 hours, by expectancy; raw parameters as the user sent them.</summary>
public sealed record GetPairIdeasQuery(string Category, string Symbol) : IRequest<GetPairIdeasResult>;