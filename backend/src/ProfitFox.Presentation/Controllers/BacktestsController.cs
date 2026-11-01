using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Backtests.Commands.CancelBacktest;
using ProfitFox.Application.Backtests.Commands.CreateBacktest;
using ProfitFox.Application.Backtests.Queries.GetBacktest;
using ProfitFox.Application.Backtests.Queries.GetBacktestResults;
using ProfitFox.Application.Backtests.Queries.GetBotBacktests;
using ProfitFox.Application.Backtests.Queries.GetResultDetails;
using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
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
        GetBacktestResult? result = await sender.Send(new GetBacktestQuery(id), cancellationToken);

        return result is null ? NotFound() : Ok(BacktestResponse.Of(result.Backtest, result.BotChanged));
    }

    [HttpGet("{id:guid}/results")]
    public async Task<IActionResult> Results(Guid id, [FromQuery] ResultsQueryParameters query, CancellationToken cancellationToken)
    {
        try
        {
            GetBacktestResultsResult? page = await sender.Send(new GetBacktestResultsQuery(id, query.Sort, query.Order, query.Offset, query.Limit), cancellationToken);

            return page is null ? NotFound() : Ok(new BacktestResultsResponse(page.Total, [.. page.Items.Select(BacktestResultResponse.Of)]));
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    [HttpGet("{id:guid}/results/{combination:long}/details")]
    public async Task<IActionResult> Details(Guid id, long combination, CancellationToken cancellationToken)
    {
        try
        {
            BacktestDetails? details = await sender.Send(new GetResultDetailsQuery(id, combination), cancellationToken);

            return details is null ? NotFound() : Ok(BacktestDetailsResponse.Of(details));
        }
        catch (ConflictException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
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