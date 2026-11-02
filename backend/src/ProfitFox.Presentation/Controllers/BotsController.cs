using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Bots.Commands.CreateBot;
using ProfitFox.Application.Bots.Commands.DeleteBot;
using ProfitFox.Application.Bots.Commands.UpdateBot;
using ProfitFox.Application.Bots.Queries.GetBot;
using ProfitFox.Application.Bots.Queries.GetBots;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Presentation.Contracts.Bots;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

[ApiController]
[Route("api/bots")]
public sealed class BotsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BotSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        IReadOnlyList<BotListItem> bots = await sender.Send(new GetBotsQuery(), cancellationToken);

        return Ok(bots.Select(BotSummaryResponse.Of));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<BotResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        Bot? bot = await sender.Send(new GetBotQuery(id), cancellationToken);

        return bot is null ? NotFound() : Ok(BotResponse.Of(bot));
    }

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create(BotBody body, CancellationToken cancellationToken)
    {
        try
        {
            Guid id = await sender.Send(
                new CreateBotCommand(body.Name, body.StrategyId ?? Guid.Empty, body.ParameterValues, body.Category, body.Symbol, body.Interval, body.Balance!.Value),
                cancellationToken);

            return CreatedAtAction(nameof(Get), new { id }, new CreatedResponse(id));
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Update(Guid id, BotBody body, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new UpdateBotCommand(id, body.Name, body.ParameterValues, body.Category, body.Symbol, body.Interval, body.Balance!.Value), cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new DeleteBotCommand(id), cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }
}