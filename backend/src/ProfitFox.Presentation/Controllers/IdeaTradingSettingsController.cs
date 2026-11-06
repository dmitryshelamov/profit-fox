using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.IdeaTrades.Commands.SaveIdeaTradingSettings;
using ProfitFox.Application.IdeaTrades.Queries.GetIdeaTradingSettings;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Presentation.Contracts.IdeaTrades;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

/// <summary>The one set of settings of trading by ideas.</summary>
[ApiController]
[Route("api/idea-trading/settings")]
public sealed class IdeaTradingSettingsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IdeaTradingSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        return Ok(IdeaTradingSettingsResponse.Of(await sender.Send(new GetIdeaTradingSettingsQuery(), cancellationToken)));
    }

    /// <summary>Replaces the settings; a value out of range is a 400 on its own field.</summary>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Put(IdeaTradingSettingsBody body, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new SaveIdeaTradingSettingsCommand(new IdeaTradingSettings(body.AccountId, body.Amount, body.MaxAmount, body.MaxOpenTrades, body.MaxTotalRisk, body.MinRiskReward)), cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }
}