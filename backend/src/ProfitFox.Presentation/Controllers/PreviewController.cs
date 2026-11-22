using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Preview;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Errors.Converters;
using ProfitFox.Presentation.Contracts.Preview;
using ProfitFox.Presentation.Contracts.Preview.Converters;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>POST /api/preview</c> (spec signal-preview): the events of a signal on one pair over exploration;
///     <c>POST /api/preview/strategy</c> (spec strategy-preview): the orders of a strategy and their trades.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PreviewController(ISender sender) : ControllerBase
{
    [HttpPost(Name = "PreviewSignal")]
    [ProducesResponseType<PreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PreviewResponse>> Post(PreviewRequest request, CancellationToken cancellation)
    {
        try
        {
            PreviewOutcomeInternal outcome = await sender.Send(new PreviewSignalQuery(request.Instrument, request.Timeframe, request.Signal), cancellation);
            return outcome.Preview is { } found
                ? PreviewResponses.Of(found)
                : BadRequest(new ErrorResponse([.. outcome.Errors.Select(RequestErrors.Of)]));
        }
        catch (HistoryUnavailableException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    [HttpPost("strategy", Name = "PreviewStrategy")]
    [ProducesResponseType<StrategyPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<StrategyPreviewResponse>> PostStrategy(PreviewStrategyRequest request, CancellationToken cancellation)
    {
        try
        {
            StrategyPreviewOutcomeInternal outcome = await sender.Send(new PreviewStrategyQuery(request.Instrument, request.Timeframe, request.Strategy, request.TakerFee, request.MakerFee), cancellation);
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
