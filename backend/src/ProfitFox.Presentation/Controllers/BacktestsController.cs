using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Backtests.Commands.CancelBacktest;
using ProfitFox.Application.Backtests.Commands.CreateBacktest;
using ProfitFox.Application.Backtests.Queries.GetBacktest;
using ProfitFox.Application.Backtests.Queries.GetBotBacktests;
using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Presentation.Contracts.Backtests;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

[ApiController]
[Route("api/backtests")]
public sealed class BacktestsController(ISender sender) : ControllerBase
{
    [HttpPost("/api/bots/{botId:guid}/backtests")]
    public async Task<IActionResult> Create(Guid botId, CreateBacktestBody body, CancellationToken cancellationToken)
    {
        try
        {
            Guid id = await sender.Send(
                new CreateBacktestCommand(
                    botId,
                    body.From,
                    body.To,
                    body.Parameters,
                    body.SearchMode,
                    body.Simulation?.Balance,
                    body.Simulation?.LimitFeePercent,
                    body.Simulation?.MarketFeePercent,
                    body.Simulation?.SlippagePercent,
                    body.Simulation?.LimitFillMode),
                cancellationToken);

            return CreatedAtAction(nameof(Get), new { id }, new CreatedResponse(id));
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    [HttpGet("/api/bots/{botId:guid}/backtests")]
    public async Task<IActionResult> ListOfBot(Guid botId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Backtest>? backtests = await sender.Send(new GetBotBacktestsQuery(botId), cancellationToken);

        return backtests is null ? NotFound() : Ok(backtests.Select(BacktestSummaryResponse.Of));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        Backtest? backtest = await sender.Send(new GetBacktestQuery(id), cancellationToken);

        return backtest is null ? NotFound() : Ok(BacktestResponse.Of(backtest));
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new CancelBacktestCommand(id), cancellationToken);

            return NoContent();
        }
        catch (ConflictException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }
}