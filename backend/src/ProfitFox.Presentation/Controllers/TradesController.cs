using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Commands.Trades.CloseTrade;
using ProfitFox.Application.Commands.Trades.OpenTrade;
using ProfitFox.Application.Commands.Trades.ResolveTrade;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Filters;
using ProfitFox.Application.Queries.Trades;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Errors.Converters;
using ProfitFox.Presentation.Contracts.Trades;
using ProfitFox.Presentation.Contracts.Trades.Converters;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>/api/trades</c> (change take-idea-trades, design.md, decision 13; spec idea-trades): the count of an entry, the entry,
///     the lists of the open and the closed trades, «Закрыть по рынку» and «Разобрана». A refused entry answers 422 with the
///     checks that did not pass; a trade or an idea that is not there 404; a trade its state does not allow 409; Bybit that did
///     not answer 503 and another refusal of Bybit 502. The API reads Bybit and sends no order: the trading Worker does.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TradesController(ISender sender) : ControllerBase
{
    [HttpPost("preview", Name = "PreviewEntry")]
    [ProducesResponseType<EntryPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<EntryPreviewResponse>> Preview(EntryRequest request, CancellationToken cancellation)
    {
        return await Answer<EntryPreviewResponse>(async () => TradeResponses.Of(await sender.Send(new PreviewEntryQuery(request.IdeaId, request.AccountId, request.Amount), cancellation)));
    }

    [HttpPost(Name = "OpenTrade")]
    [ProducesResponseType<TradeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<EntryRefusedResponse>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TradeResponse>> Open(EntryRequest request, CancellationToken cancellation)
    {
        return await Answer<TradeResponse>(async () =>
        {
            Trade trade = await sender.Send(new OpenTradeCommand(request.IdeaId, request.AccountId, request.Amount), cancellation);
            return CreatedAtRoute("GetTrade", new { id = trade.Id }, TradeResponses.Of(trade));
        });
    }

    /// <param name="state"><c>open</c> (by default) or <c>closed</c>.</param>
    /// <param name="sort">A column of the list: the click from the newest for the open, the exit for the closed by default.</param>
    /// <param name="direction"><c>asc</c> or <c>desc</c>.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">50, 100 or 200.</param>
    /// <param name="cancellation">The request was given up.</param>
    [HttpGet(Name = "ListTrades")]
    [ProducesResponseType<TradePageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TradePageResponse>> List(
        [FromQuery] string? state,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellation)
    {
        if (state is not (null or "open" or "closed"))
        {
            return UnprocessableEntity(new ErrorResponse([new RequestError("InvalidState", "state — open или closed") { Field = "state" }]));
        }

        return await Answer<TradePageResponse>(async () =>
        {
            TradeListResult result = await sender.Send(new GetTradesQuery(state != "closed", new ListRequest(sort, direction, page, pageSize)), cancellation);
            return new TradePageResponse(
                [.. result.Page.Items.Select(item => TradeResponses.Of(item.Trade, item.IdeaResult))],
                result.Page.Total,
                result.Page.Page,
                result.Page.PageSize,
                result.Open,
                result.Closed,
                decimal.Round(result.ClosedResult, 2),
                decimal.Round(result.ClosedFees, 2));
        });
    }

    [HttpGet("{id:guid}", Name = "GetTrade")]
    [ProducesResponseType<TradeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TradeResponse>> Get(Guid id, CancellationToken cancellation)
    {
        return await Answer<TradeResponse>(async () => TradeResponses.Of(await sender.Send(new GetTradeQuery(id), cancellation)));
    }

    /// <summary>
    ///     «Закрыть по рынку»: the close order is written, and the trading Worker sends it.
    /// </summary>
    [HttpPost("{id:guid}/close", Name = "CloseTrade")]
    [ProducesResponseType<TradeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TradeResponse>> Close(Guid id, CancellationToken cancellation)
    {
        return await Answer<TradeResponse>(async () => TradeResponses.Of(await sender.Send(new CloseTradeCommand(id), cancellation)));
    }

    /// <summary>
    ///     «Разобрана»: a trade in «Ошибка» whose position is gone at Bybit is closed «на бирже». A refusal answers 409; when the
    ///     check is impossible — Bybit no longer takes the key or the secret does not read — the problem has the extension
    ///     <c>canSkipCheck</c>, and the owner confirms the position on Bybit closed with <paramref name="unchecked" />.
    /// </summary>
    /// <param name="id">The trade.</param>
    /// <param name="unchecked">The owner confirms the position on Bybit is closed; taken only when the check is impossible.</param>
    /// <param name="cancellation">The request was given up.</param>
    [HttpPost("{id:guid}/resolve", Name = "ResolveTrade")]
    [ProducesResponseType<TradeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TradeResponse>> Resolve(Guid id, [FromQuery] bool? @unchecked, CancellationToken cancellation)
    {
        try
        {
            return await Answer<TradeResponse>(async () => TradeResponses.Of(await sender.Send(new ResolveTradeCommand(id, @unchecked == true), cancellation)));
        }
        catch (TradeUncheckedException exception)
        {
            ProblemDetails problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, StatusCodes.Status409Conflict, detail: exception.Message);
            problem.Extensions["canSkipCheck"] = true;
            return Conflict(problem);
        }
    }

    private async Task<ActionResult<T>> Answer<T>(Func<Task<ActionResult<T>>> act)
    {
        try
        {
            return await act();
        }
        catch (TradeEntryRefusedException exception)
        {
            return UnprocessableEntity(new EntryRefusedResponse([.. exception.Failed.Select(TradeResponses.Of)]));
        }
        catch (IdeasRequestException exception)
        {
            return UnprocessableEntity(new ErrorResponse([.. exception.Errors.Select(RequestErrors.Of)]));
        }
        catch (TradeConflictException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (KeyNotFoundException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (ExchangeUnavailableException)
        {
            return Problem("Bybit не отвечает: попробуйте ещё раз позже.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (SecretUnreadableException)
        {
            return Problem("Секрет аккаунта не читается: удалите аккаунт и добавьте его заново.", statusCode: StatusCodes.Status409Conflict);
        }
        catch (ExchangeException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private async Task<ActionResult<T>> Answer<T>(Func<Task<T>> act)
    {
        return await Answer<T>(async () => (ActionResult<T>)await act());
    }
}
