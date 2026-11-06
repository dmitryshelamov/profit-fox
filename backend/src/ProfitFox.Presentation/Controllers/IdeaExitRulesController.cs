using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Ideas.Commands.ApplyExitRule;
using ProfitFox.Application.Ideas.Queries.GetExitRulePreview;
using ProfitFox.Application.Ideas.Queries.GetExitRules;
using ProfitFox.Presentation.Contracts.Ideas;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

/// <summary>The exit rules of the ideas: the lifetime, the target and the entry window of each type × interval, their preview and their applying.</summary>
[ApiController]
[Route("api/ideas/exit-rules")]
public sealed class IdeaExitRulesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IdeaExitRulesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        return Ok(IdeaExitRulesResponse.Of(await sender.Send(new GetExitRulesQuery(), cancellationToken)));
    }

    /// <summary>
    ///     What a candidate rule would have done over the last 90 and 30 days against the rule now; a parameter the type has no use for or a value
    ///     off the grid is a 400 on its name.
    /// </summary>
    [HttpGet("preview")]
    [ProducesResponseType<ExitRulePreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Preview(
        [FromQuery] string? type,
        [FromQuery] string? interval,
        [FromQuery] int life,
        [FromQuery] decimal? target,
        [FromQuery] int? window,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(ExitRulePreviewResponse.Of(await sender.Send(new GetExitRulePreviewQuery(type, interval, life, target, window), cancellationToken)));
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    /// <summary>Applies the rule to the ideas of the type × interval made from now on; answers with the rule saved and when it was applied.</summary>
    [HttpPut("{type}/{interval}")]
    [ProducesResponseType<IdeaExitRuleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Apply(string type, string interval, ApplyExitRuleBody body, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(IdeaExitRuleResponse.Of(await sender.Send(new ApplyExitRuleCommand(type, interval, body.Life, body.Target, body.Window), cancellationToken)));
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }
}