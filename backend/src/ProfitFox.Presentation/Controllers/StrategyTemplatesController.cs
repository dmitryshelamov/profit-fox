using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Strategies.Queries.GetStrategyTemplate;
using ProfitFox.Application.Strategies.Queries.GetStrategyTemplates;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

/// <summary>The built-in strategy templates. A strategy made from a template is a new strategy: the definition goes to the strategies as it is.</summary>
[ApiController]
[Route("api/strategy-templates")]
public sealed class StrategyTemplatesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<StrategyTemplateSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        IReadOnlyList<StrategyTemplateSummary> templates = await sender.Send(new GetStrategyTemplatesQuery(), cancellationToken);

        return Ok(templates.Select(StrategyTemplateSummaryResponse.Of));
    }

    [HttpGet("{code}")]
    [ProducesResponseType<StrategyTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string code, CancellationToken cancellationToken)
    {
        StrategyTemplateResult? template = await sender.Send(new GetStrategyTemplateQuery(code), cancellationToken);

        return template is null ? NotFound() : Ok(StrategyTemplateResponse.Of(template));
    }
}