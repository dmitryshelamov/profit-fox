using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.IdeaAutos.Queries.GetIdeaGuards;
using ProfitFox.Application.IdeaTrades.Commands.SaveIdeaTradingSettings;
using ProfitFox.Application.IdeaTrades.Queries.GetIdeaTradingSettings;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Presentation.Contracts.IdeaAutos;
using ProfitFox.Presentation.Contracts.IdeaTrades;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

/// <summary>The one set of settings of trading by ideas.</summary>
[ApiController]
[Route("api/idea-trading")]
public sealed class IdeaTradingSettingsController(ISender sender) : ControllerBase
{
    [HttpGet("settings")]
    [ProducesResponseType<IdeaTradingSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        return Ok(IdeaTradingSettingsResponse.Of(await sender.Send(new GetIdeaTradingSettingsQuery(), cancellationToken)));
    }

    /// <summary>Replaces the settings; a value out of range is a 400 on its own field.</summary>
    [HttpPut("settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Put(IdeaTradingSettingsBody body, CancellationToken cancellationToken)
    {
        try
        {
            if (body.Leverage != decimal.Truncate(body.Leverage))
            {
                throw new ValidationFailedException(new Dictionary<string, string> { ["leverage"] = "The leverage must be a whole number." });
            }

            await sender.Send(
                new SaveIdeaTradingSettingsCommand(new IdeaTradingSettings(
                    body.AccountId, body.Amount, body.MaxAmount, body.MaxOpenTrades, body.MaxTotalRisk, body.MinRiskReward, body.DailyLossLimit, body.MaxSameDirection, body.AutosBudget,
                    (int)body.Leverage)),
                cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    /// <summary>The state of the guards over the real trades: what is open against its limits and the loss of the day against the daily stop.</summary>
    [HttpGet("guards")]
    [ProducesResponseType<IdeaGuardsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Guards(CancellationToken cancellationToken)
    {
        return Ok(IdeaGuardsResponse.Of(await sender.Send(new GetIdeaGuardsQuery(), cancellationToken)));
    }
}