using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Commands.Runs.CancelRun;
using ProfitFox.Application.Commands.Runs.EnqueueRun;
using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Runs;
using ProfitFox.Domain.Entities.Runs.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Runs;
using ProfitFox.Presentation.Contracts.Runs.Converters;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>/api/runs</c> (spec bench-run-queue): puts runs in the queue, lists the runs that wait, go and ended, tells one run
///     with its line and report, and cancels a run. The API does not compute a run: a Worker does.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RunsController(ISender sender) : ControllerBase
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 200;

    /// <summary>
    ///     Puts a run in the queue. The rules of the ledger are checked now, so a refusal comes at once and leaves no line.
    /// </summary>
    [HttpPost(Name = "EnqueueRun")]
    [ProducesResponseType<RunResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<RunConflictResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RunResponse>> Post(CreateRunRequest request, CancellationToken cancellation)
    {
        List<RequestError> errors = [];
        if (string.IsNullOrWhiteSpace(request.Hypothesis))
        {
            errors.Add(new RequestError("MissingField", "the hypothesis is required") { Field = "hypothesis" });
        }

        Period period = default;
        if (request.Period is null || !Periods.TryParse(request.Period, out period))
        {
            errors.Add(new RequestError("InvalidField", "the period is exploration, holdout or new-data") { Field = "period" });
        }

        if (request.Kind is not (RunKinds.Verdict or RunKinds.Parity))
        {
            errors.Add(new RequestError("InvalidField", $"the kind is {RunKinds.Verdict} or {RunKinds.Parity}") { Field = "kind" });
        }

        if (errors.Count > 0)
        {
            return BadRequest(new ErrorResponse(errors));
        }

        try
        {
            RunInternal run = await sender.Send(new EnqueueRunCommand(request.Hypothesis!, request.Version, period, request.Kind == RunKinds.Parity), cancellation);
            return CreatedAtRoute("GetRun", new { id = run.Id }, RunResponses.Of(run));
        }
        catch (HypothesisNotFoundException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (HypothesisException exception)
        {
            return UnprocessableEntity(new ErrorResponse([.. exception.Errors.Select(error => new RequestError("InvalidHypothesis", error))]));
        }
        catch (LedgerException exception)
        {
            return Conflict(new RunConflictResponse(exception.Message, exception.RunId));
        }
    }

    /// <summary>
    ///     The runs that wait and go in the order of the queue, or with <c>active=false</c> the latest runs that ended, the newest
    ///     first (at most <c>limit</c>, 20 when it is missing and 200 at most).
    /// </summary>
    [HttpGet(Name = "ListRuns")]
    [ProducesResponseType<RunResponse[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RunResponse[]>> List([FromQuery] bool active = true, [FromQuery] int? limit = null, CancellationToken cancellation = default)
    {
        if (active)
        {
            return (await sender.Send(new GetActiveRunsQuery(), cancellation)).Select(RunResponses.Of).ToArray();
        }

        return (await sender.Send(new GetFinishedRunsQuery(Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit)), cancellation)).Select(RunResponses.Of).ToArray();
    }

    /// <summary>
    ///     One run with its progress, and once it ended its line of the ledger and its report.
    /// </summary>
    [HttpGet("{id}", Name = "GetRun")]
    [ProducesResponseType<RunResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunResponse>> Get(string id, CancellationToken cancellation)
    {
        return await sender.Send(new GetRunQuery(id), cancellation) is { } details
            ? RunResponses.Of(details)
            : Problem($"There is no run {id}.", statusCode: StatusCodes.Status404NotFound);
    }

    /// <summary>
    ///     Takes a waiting run out of the queue, or asks the Worker of a going run to stop it; the run ends with a
    ///     <c>cancelled</c> line within seconds.
    /// </summary>
    [HttpPost("{id}/cancel", Name = "CancelRun")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<RunConflictResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Cancel(string id, CancellationToken cancellation)
    {
        CancelOutcome outcome = await sender.Send(new CancelRunCommand(id), cancellation);
        return outcome switch
        {
            CancelOutcome.Removed or CancelOutcome.Requested => NoContent(),
            CancelOutcome.AlreadyFinished => Conflict(new RunConflictResponse($"Run {id} has ended already.", id)),
            _ => Problem($"There is no run {id}.", statusCode: StatusCodes.Status404NotFound)
        };
    }
}
