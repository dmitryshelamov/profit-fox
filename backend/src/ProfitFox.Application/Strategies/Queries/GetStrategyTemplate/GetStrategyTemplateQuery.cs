using MediatR;

namespace ProfitFox.Application.Strategies.Queries.GetStrategyTemplate;

/// <summary>A template by its code, or <c>null</c> when there is none.</summary>
public sealed record GetStrategyTemplateQuery(string Code) : IRequest<StrategyTemplateResult?>;