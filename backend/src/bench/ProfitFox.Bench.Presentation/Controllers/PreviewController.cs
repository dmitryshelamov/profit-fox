using Microsoft.AspNetCore.Mvc;
using ProfitFox.Bench.MarketData;
using ProfitFox.Bench.Presentation.Errors;
using ProfitFox.Bench.Presentation.Preview;

namespace ProfitFox.Bench.Presentation.Controllers;

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
            PreviewResult result = await preview.Run(request, cancellation);
            return result.Preview is { } found ? found : BadRequest(result.Refused);
        }
        catch (HistoryUnavailableException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
