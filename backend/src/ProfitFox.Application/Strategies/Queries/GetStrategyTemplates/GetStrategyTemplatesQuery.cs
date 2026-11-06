using MediatR;

namespace ProfitFox.Application.Strategies.Queries.GetStrategyTemplates;

/// <summary>The built-in templates in the order of the catalog.</summary>
public sealed record GetStrategyTemplatesQuery : IRequest<IReadOnlyList<StrategyTemplateSummary>>;