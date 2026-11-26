using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Commands.Backtests.CancelBacktest;
using ProfitFox.Application.Commands.Backtests.EnqueueBacktest;
using ProfitFox.Application.Commands.Backtests.EnqueueRest;
using ProfitFox.Application.Contracts.Backtests;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Backtests;
using ProfitFox.Domain.Entities.Runs.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Presentation.Contracts.Backtests;
using ProfitFox.Presentation.Contracts.Backtests.Converters;
using ProfitFox.Presentation.Contracts.Errors;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>/api/backtests</c> (spec strategy-backtest, spec bench-run-queue; change add-strategy-backtest, design.md, decision
///     9): puts a backtest of a version of a strategy in the queue, lists the backtests that wait, go and ended, tells one
///     backtest with its sets and a set with its pairs, puts a set on the rest of history and cancels a backtest. The API
///     does not compute a backtest: a Worker does.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class BacktestsController(ISender sender) : ControllerBase
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 200;

    /// <summary>
    ///     Puts a backtest in the queue. The request is checked whole now: a refusal comes at once with every error and
    ///     queues nothing.
    /// </summary>
    [HttpPost(Name = "EnqueueBacktest")]
    [ProducesResponseType<BacktestResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<BacktestResponse>> Post(CreateBacktestRequest request, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(request.Strategy))
        {
            return BadRequest(new ErrorResponse([new RequestError("MissingField", "the strategy is required") { Field = "strategy" }]));
        }

        var asked = new BacktestRequest(
            request.Values?.ToDictionary(pair => pair.Key, IReadOnlyList<string> (pair) => pair.Value, StringComparer.Ordinal),
            request.Timeframes,
            request.Start,
            request.End,
            request.Pairs,
            request.TakerFee,
            request.MakerFee);
        return await Created(() => sender.Send(new EnqueueBacktestCommand(request.Strategy, request.Version, asked), cancellation));
    }

    /// <summary>
    ///     The backtests that wait and go in the order of the queue, or with <c>active=false</c> the latest that ended, the
    ///     newest first (at most <c>limit</c>, 20 when it is missing and 200 at most), without their sets.
    /// </summary>
    [HttpGet(Name = "ListBacktests")]
    [ProducesResponseType<BacktestResponse[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BacktestResponse[]>> List([FromQuery] bool active = true, [FromQuery] int? limit = null, CancellationToken cancellation = default)
    {
        IReadOnlyList<BacktestInternal> found = await sender.Send(new GetBacktestsQuery(active, Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit)), cancellation);
        return found.Select(BacktestResponses.Of).ToArray();
    }

    /// <summary>
    ///     One backtest with its settings, state, progress and sets; the numbers of the sets once it is done.
    /// </summary>
    [HttpGet("{id}", Name = "GetBacktest")]
    [ProducesResponseType<BacktestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BacktestResponse>> Get(string id, CancellationToken cancellation)
    {
        return await sender.Send(new GetBacktestQuery(id), cancellation) is { } backtest
            ? BacktestResponses.Of(backtest)
            : Problem($"There is no backtest {id}.", statusCode: StatusCodes.Status404NotFound);
    }

    /// <summary>
    ///     A set of a backtest and its pairs with orders, by the sum of their results from the highest.
    /// </summary>
    [HttpGet("{id}/sets/{number:int}", Name = "GetBacktestSet")]
    [ProducesResponseType<BacktestSetPairsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BacktestSetPairsResponse>> GetSet(string id, int number, CancellationToken cancellation)
    {
        if (await sender.Send(new GetBacktestQuery(id), cancellation) is not { } backtest
            || backtest.Sets.FirstOrDefault(set => set.Set.Number == number) is not { } set
            || await sender.Send(new GetBacktestPairsQuery(id, number), cancellation) is not { } pairs)
        {
            return Problem($"The backtest {id} has no set {number}.", statusCode: StatusCodes.Status404NotFound);
        }

        return new BacktestSetPairsResponse(BacktestResponses.Of(set, backtest), [.. pairs.Select(BacktestResponses.Of)]);
    }

    /// <summary>
    ///     Puts a set of a done backtest in the queue on the rest of history: from the end of the backtest to the start of
    ///     the current UTC day.
    /// </summary>
    [HttpPost("{id}/sets/{number:int}/rest", Name = "EnqueueBacktestRest")]
    [ProducesResponseType<BacktestResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BacktestResponse>> PostRest(string id, int number, CancellationToken cancellation)
    {
        return await Created(() => sender.Send(new EnqueueRestCommand(id, number), cancellation));
    }

    /// <summary>
    ///     Takes a waiting backtest out of the queue, or asks the Worker of a going one to stop it; it ends cancelled within
    ///     seconds.
    /// </summary>
    [HttpPost("{id}/cancel", Name = "CancelBacktest")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Cancel(string id, CancellationToken cancellation)
    {
        CancelOutcome outcome = await sender.Send(new CancelBacktestCommand(id), cancellation);
        return outcome switch
        {
            CancelOutcome.Removed or CancelOutcome.Requested => NoContent(),
            CancelOutcome.AlreadyFinished => Problem($"The backtest {id} has ended already.", statusCode: StatusCodes.Status409Conflict),
            _ => Problem($"There is no backtest {id}.", statusCode: StatusCodes.Status404NotFound)
        };
    }

    private async Task<ActionResult<BacktestResponse>> Created(Func<Task<BacktestInternal>> enqueue)
    {
        try
        {
            BacktestInternal backtest = await enqueue();
            return CreatedAtRoute("GetBacktest", new { id = backtest.Id }, BacktestResponses.Of(backtest));
        }
        catch (StrategyLibraryException exception) when (exception.Problem == StrategyProblem.NotFound)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (StrategyLibraryException exception)
        {
            return UnprocessableEntity(new ErrorResponse([new RequestError("UnreadableStrategy", exception.Message)]));
        }
        catch (BacktestNotFoundException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (BacktestRequestException exception)
        {
            return UnprocessableEntity(new ErrorResponse([.. exception.Errors.Select(BacktestResponses.Of)]));
        }
        catch (HistoryUnavailableException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}