using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Commands.Trades.LiftHalt;
using ProfitFox.Application.Commands.Trades.SaveTradeSettings;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Trades;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Trades;
using ProfitFox.Presentation.Contracts.Trades.Converters;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>/api/risk</c> (change take-idea-trades, design.md, decision 13; spec trade-risk): the state of the risk module, its
///     settings and «Снять запрет». A refused setting answers 422 at its field; a lift while a trade still closes 409. Bybit is
///     not called.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RiskController(ISender sender) : ControllerBase
{
    [HttpGet("state", Name = "GetRiskState")]
    [ProducesResponseType<RiskStateResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RiskStateResponse>> State(CancellationToken cancellation)
    {
        return TradeResponses.Of(await sender.Send(new GetRiskStateQuery(), cancellation));
    }

    [HttpGet("settings", Name = "GetRiskSettings")]
    [ProducesResponseType<TradeSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TradeSettingsResponse>> Settings(CancellationToken cancellation)
    {
        return TradeResponses.Of(await sender.Send(new GetTradeSettingsQuery(), cancellation));
    }

    [HttpPut("settings", Name = "SaveRiskSettings")]
    [ProducesResponseType<TradeSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TradeSettingsResponse>> Save(TradeSettingsRequest request, CancellationToken cancellation)
    {
        try
        {
            TradeSettings saved = await sender.Send(new SaveTradeSettingsCommand(TradeResponses.Of(request)), cancellation);
            return TradeResponses.Of(saved);
        }
        catch (TradeSettingsRequestException exception)
        {
            return UnprocessableEntity(new ErrorResponse([.. exception.Problems.Select(problem => new RequestError(problem.Code, problem.Reason) { Field = problem.Field })]));
        }
    }

    /// <summary>
    ///     «Снять запрет»: entries are allowed again, and the loss of the day counts from now.
    /// </summary>
    [HttpPost("halt/lift", Name = "LiftHalt")]
    [ProducesResponseType<HaltResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HaltResponse>> Lift(CancellationToken cancellation)
    {
        try
        {
            return TradeResponses.Of(await sender.Send(new LiftHaltCommand(), cancellation), []);
        }
        catch (TradeConflictException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}
