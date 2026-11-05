using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies.Queries.GetStrategyTemplate;

/// <param name="Code"></param>
/// <param name="Name"></param>
/// <param name="Description"></param>
/// <param name="Definition">Without a layout.</param>
public sealed record StrategyTemplateResult(string Code, string Name, string Description, StrategyDefinition Definition);