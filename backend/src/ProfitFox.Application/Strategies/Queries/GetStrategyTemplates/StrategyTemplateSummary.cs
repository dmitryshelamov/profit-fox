using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Application.Strategies.Queries.GetStrategyTemplates;

/// <param name="Code"></param>
/// <param name="Name"></param>
/// <param name="Description"></param>
/// <param name="Direction"></param>
public sealed record StrategyTemplateSummary(string Code, string Name, string Description, StrategyDirection Direction);