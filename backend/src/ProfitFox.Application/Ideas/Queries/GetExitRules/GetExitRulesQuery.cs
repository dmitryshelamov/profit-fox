using MediatR;

namespace ProfitFox.Application.Ideas.Queries.GetExitRules;

/// <summary>The exit rules of every type × interval, the defaults among them, and the grid of each interval.</summary>
public sealed record GetExitRulesQuery : IRequest<GetExitRulesResult>;