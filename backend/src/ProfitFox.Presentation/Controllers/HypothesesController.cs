using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using ProfitFox.Application.Hypotheses;
using ProfitFox.Application.Runs;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Presentation.Errors;
using ProfitFox.Presentation.Hypotheses;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>/api/hypotheses</c> (specs signal-preview «Список гипотез» and hypothesis-bench «Заморозка гипотезы»): the latest
///     versions of the hypotheses with what can be run on them, and a text loaded as a new version.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HypothesesController(HypothesisCatalog hypotheses, RunBoard board) : ControllerBase
{
    [HttpGet(Name = "ListHypotheses")]
    [ProducesResponseType<HypothesisResponse[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HypothesisResponse[]>> Get(CancellationToken cancellation)
    {
        IReadOnlyList<HypothesisItem> items = await hypotheses.List(cancellation);
        IReadOnlyDictionary<string, IReadOnlyList<RunAvailability>> availability = await board.Availability(items, cancellation);
        return items
            .Select(item => new HypothesisResponse(
                item.Id,
                item.Version,
                item.Name,
                item.Timeframe is { } timeframe ? Timeframes.Name(timeframe) : null,
                item.Signal,
                item.Error,
                [.. availability[item.Id].Select(run => new RunAvailabilityResponse(run.Period, run.Kind, run.CanRun, run.Reason, run.RunId))]))
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
            HypothesisAdded added = await hypotheses.Add(text ?? "", cancellation);
            var response = new HypothesisAddedResponse(added.Hypothesis.Id, added.Hypothesis.Name, added.Hypothesis.Version);
            return added.Created ? StatusCode(StatusCodes.Status201Created, response) : response;
        }
        catch (HypothesisException exception)
        {
            return UnprocessableEntity(new ErrorResponse([.. exception.Errors.Select(error => new RequestError("InvalidHypothesis", error))]));
        }
    }
}
