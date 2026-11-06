using ProfitFox.Application.Strategies;
using ProfitFox.Application.Strategies.Queries.GetStrategyTemplates;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <param name="Code"></param>
/// <param name="Name"></param>
/// <param name="Description">The idea of the strategy in one phrase.</param>
/// <param name="Direction"><c>long</c> or <c>short</c>.</param>
public sealed record StrategyTemplateSummaryResponse(string Code, string Name, string Description, string Direction)
{
    public static StrategyTemplateSummaryResponse Of(StrategyTemplateSummary template)
    {
        return new StrategyTemplateSummaryResponse(template.Code, template.Name, template.Description, StrategyCodes.ToCode(template.Direction));
    }
}