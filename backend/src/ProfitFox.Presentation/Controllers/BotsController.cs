using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Bots.Commands.CreateBot;
using ProfitFox.Application.Bots.Commands.DeleteBot;
using ProfitFox.Application.Bots.Commands.UpdateBot;
using ProfitFox.Application.Bots.Queries.GetBot;
using ProfitFox.Application.Bots.Queries.GetBots;
using ProfitFox.Application.LiveTrading.Commands.ClosePosition;
using ProfitFox.Application.LiveTrading.Commands.PauseBot;
using ProfitFox.Application.LiveTrading.Commands.ResumeBot;
using ProfitFox.Application.LiveTrading.Commands.StartBot;
using ProfitFox.Application.LiveTrading.Commands.StopBot;
using ProfitFox.Application.LiveTrading.Queries.GetBotPositions;
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
        GetBotResult? bot = await sender.Send(new GetBotQuery(id), cancellationToken);

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
                new CreateBotCommand(body.Name, body.StrategyId ?? Guid.Empty, body.ParameterValues, body.Category, body.Symbol, body.Interval, body.Balance!.Value, body.ExchangeAccountId),
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
            await sender.Send(new UpdateBotCommand(id, body.Name, body.ParameterValues, body.Category, body.Symbol, body.Interval, body.Balance!.Value, body.ExchangeAccountId), cancellationToken);

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

    /// <summary>The positions of the bot in live trading, the newest first, with their orders and fills.</summary>
    [HttpGet("{id:guid}/positions")]
    [ProducesResponseType<IReadOnlyList<LivePositionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Positions(Guid id, CancellationToken cancellationToken)
    {
        IReadOnlyList<LivePositionDetails>? positions = await sender.Send(new GetBotPositionsQuery(id), cancellationToken);

        return positions is null ? NotFound() : Ok(positions.Select(LivePositionResponse.Of));
    }

    /// <summary>Starts a stopped bot on the latest version of its strategy; a conflict says why the account cannot trade it.</summary>
    [HttpPost("{id:guid}/start")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
    {
        return ActAsync(new StartBotCommand(id), cancellationToken);
    }

    /// <summary>Pauses a running bot: its strategy stops reacting, its orders and position stay.</summary>
    [HttpPost("{id:guid}/pause")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Pause(Guid id, CancellationToken cancellationToken)
    {
        return ActAsync(new PauseBotCommand(id), cancellationToken);
    }

    [HttpPost("{id:guid}/resume")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Resume(Guid id, CancellationToken cancellationToken)
    {
        return ActAsync(new ResumeBotCommand(id), cancellationToken);
    }

    /// <summary>Stops a running or paused bot: its active orders are cancelled, its position stays.</summary>
    [HttpPost("{id:guid}/stop")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Stop(Guid id, CancellationToken cancellationToken)
    {
        return ActAsync(new StopBotCommand(id), cancellationToken);
    }

    /// <summary>Closes the open position of a stopped bot at the market.</summary>
    [HttpPost("{id:guid}/close-position")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ClosePosition(Guid id, CancellationToken cancellationToken)
    {
        return ActAsync(new ClosePositionCommand(id), cancellationToken);
    }

    private async Task<IActionResult> ActAsync(IRequest command, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(command, cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }
}