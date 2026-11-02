using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Backtests.Commands.CancelBacktest;
using ProfitFox.Application.Backtests.Commands.CreateBacktest;
using ProfitFox.Application.Backtests.Queries.GetBacktest;
using ProfitFox.Application.Backtests.Queries.GetBacktestResults;
using ProfitFox.Application.Backtests.Queries.GetBotBacktests;
using ProfitFox.Application.Backtests.Queries.GetResult;
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
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
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
    [ProducesResponseType<IReadOnlyList<BacktestSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListOfBot(Guid botId, CancellationToken cancellationToken)
    {
        IReadOnlyList<GetBacktestResult>? backtests = await sender.Send(new GetBotBacktestsQuery(botId), cancellationToken);

        return backtests is null ? NotFound() : Ok(backtests.Select(item => BacktestSummaryResponse.Of(item.Backtest, item.BotChanged)));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<BacktestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        GetBacktestResult? result = await sender.Send(new GetBacktestQuery(id), cancellationToken);

        return result is null ? NotFound() : Ok(BacktestResponse.Of(result.Backtest, result.BotChanged));
    }

    [HttpGet("{id:guid}/results")]
    [ProducesResponseType<BacktestResultsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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

    [HttpGet("{id:guid}/results/{combination:long}")]
    [ProducesResponseType<BacktestResultResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Result(Guid id, long combination, CancellationToken cancellationToken)
    {
        BacktestResult? result = await sender.Send(new GetResultQuery(id, combination), cancellationToken);

        return result is null ? NotFound() : Ok(BacktestResultResponse.Of(result));
    }

    [HttpGet("{id:guid}/results/{combination:long}/details")]
    [ProducesResponseType<BacktestDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
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