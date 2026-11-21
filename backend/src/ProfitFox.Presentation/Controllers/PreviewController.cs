using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Preview;
using ProfitFox.Presentation.Errors;
using ProfitFox.Presentation.Preview;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>POST /api/preview</c> (spec signal-preview): the events of a signal on one pair over exploration.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PreviewController(SignalPreview preview) : ControllerBase
{
    [HttpPost(Name = "PreviewSignal")]
    [ProducesResponseType<PreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PreviewResponse>> Post(PreviewRequest request, CancellationToken cancellation)
    {
        try
        {
            PreviewOutcome outcome = await preview.Run(new PreviewQuery(request.Instrument, request.Timeframe, request.Signal), cancellation);
            return outcome.Preview is { } found
                ? PreviewResponses.Of(found)
                : BadRequest(new ErrorResponse([.. outcome.Errors.Select(RequestErrors.Of)]));
        }
        catch (HistoryUnavailableException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
