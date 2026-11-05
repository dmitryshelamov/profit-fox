using System.Text.Json.Nodes;

using ProfitFox.Application.Strategies;
using ProfitFox.Application.Strategies.Queries.GetStrategyTemplate;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <param name="Code"></param>
/// <param name="Name"></param>
/// <param name="Description">The idea of the strategy in one phrase.</param>
/// <param name="Definition">The definition of a strategy, with an empty layout: it goes to <c>POST /api/strategies</c> as it is.</param>
public sealed record StrategyTemplateResponse(string Code, string Name, string Description, JsonObject Definition)
{
    public static StrategyTemplateResponse Of(StrategyTemplateResult template)
    {
        return new StrategyTemplateResponse(template.Code, template.Name, template.Description, StrategyJson.WriteStrategy(template.Definition));
    }
}