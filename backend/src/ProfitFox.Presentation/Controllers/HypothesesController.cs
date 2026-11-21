using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using ProfitFox.Application.Commands.Hypotheses.AddHypothesis;
using ProfitFox.Application.Contracts.Hypotheses;
using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Application.Queries.Hypotheses;
using ProfitFox.Application.Queries.Runs;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Hypotheses;
using ProfitFox.Presentation.Contracts.Runs.Converters;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>/api/hypotheses</c> (specs signal-preview «Список гипотез», hypothesis-bench «Заморозка гипотезы» and
///     bench-run-queue «Последний прогон гипотезы»): the latest versions of the hypotheses with what can be run on them and
///     the last run of each, and a text loaded as a new version.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HypothesesController(ISender sender) : ControllerBase
{
    [HttpGet(Name = "ListHypotheses")]
    [ProducesResponseType<HypothesisResponse[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HypothesisResponse[]>> Get(CancellationToken cancellation)
    {
        IReadOnlyList<HypothesisInternal> items = await sender.Send(new GetHypothesesQuery(), cancellation);
        IReadOnlyDictionary<string, IReadOnlyList<RunAvailabilityInternal>> availability = await sender.Send(new GetRunAvailabilityQuery(items), cancellation);
        IReadOnlyDictionary<string, LastRunInternal> last = await sender.Send(new GetLastRunsQuery(items), cancellation);
        return items
            .Select(item => new HypothesisResponse(
                item.Id,
                item.Version,
                item.Name,
                item.Timeframe is { } timeframe ? Timeframes.Name(timeframe) : null,
                item.Signal,
                item.Error,
                [.. availability[item.Id].Select(run => new RunAvailabilityResponse(run.Period, run.Kind, run.CanRun, run.Reason, run.RunId))],
                last.TryGetValue(item.Id, out LastRunInternal? run) ? RunResponses.Of(run) : null))
            .ToArray();
    }

    /// <summary>
    ///     Loads the text of a <c>hypothesis.json</c>, sent as <c>text/plain</c> so that its hash is that of the bytes sent. A text
    ///     that makes a new version answers 201; a text a version already has answers 200 with that version.
    /// </summary>
    [HttpPost(Name = "AddHypothesis")]
    [Consumes("text/plain")]
    [Produces("application/json")]
    [ProducesResponseType<HypothesisAddedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HypothesisAddedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<HypothesisAddedResponse>> Post([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] string? text, CancellationToken cancellation)
    {
        try
        {
            AddHypothesisResult added = await sender.Send(new AddHypothesisCommand(text ?? ""), cancellation);
            var response = new HypothesisAddedResponse(added.Id, added.Name, added.Version);
            return added.Created ? StatusCode(StatusCodes.Status201Created, response) : response;
        }
        catch (HypothesisException exception)
        {
            return UnprocessableEntity(new ErrorResponse([.. exception.Errors.Select(error => new RequestError("InvalidHypothesis", error))]));
        }
    }
}
