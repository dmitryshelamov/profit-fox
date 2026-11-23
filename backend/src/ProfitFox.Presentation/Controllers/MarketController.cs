using System.Text.Json;

using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Filters;
using ProfitFox.Application.Queries.Ideas;
using ProfitFox.Application.Queries.Market;
using ProfitFox.Application.Services.Market;
using ProfitFox.Domain.ValueObjects.Lists.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Errors.Converters;
using ProfitFox.Presentation.Contracts.Ideas;
using ProfitFox.Presentation.Contracts.Ideas.Converters;
using ProfitFox.Presentation.Contracts.Market;
using ProfitFox.Presentation.Contracts.Market.Converters;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>/api/market</c> (change add-ideas-and-market, spec market-overview): the snapshot of the pairs a page at a time,
///     the page of a pair with its chart, its live candle as a stream and its ideas. A pair outside the set is not found.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MarketController(ISender sender, LiveCandleHub live) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <param name="sort">A column of the market; the turnover of 24 hours from the largest by default.</param>
    /// <param name="direction"><c>asc</c> or <c>desc</c>.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">50, 100 or 200.</param>
    /// <param name="search">A part of the symbol.</param>
    /// <param name="ideasOnly">Only pairs with an open idea.</param>
    /// <param name="strategy">The strategy of an open idea.</param>
    /// <param name="timeframe">The timeframe of an open idea.</param>
    /// <param name="filter">A saved filter of «Рынок».</param>
    /// <param name="cancellation">The request was given up.</param>
    [HttpGet("pairs", Name = "ListMarketPairs")]
    [ProducesResponseType<MarketPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MarketPageResponse>> List(
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] bool? ideasOnly,
        [FromQuery] string? strategy,
        [FromQuery] string? timeframe,
        [FromQuery] Guid? filter,
        CancellationToken cancellation)
    {
        var query = new GetMarketPairsQuery(new ListRequest(sort, direction, page, pageSize, filter), search, ideasOnly == true, strategy, timeframe);
        return await Answer<MarketPageResponse>(async () => MarketResponses.Of(await sender.Send(query, cancellation)));
    }

    [HttpGet("pairs/{symbol}", Name = "GetMarketPair")]
    [ProducesResponseType<MarketPairResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MarketPairResponse>> Get(string symbol, CancellationToken cancellation)
    {
        return await Answer<MarketPairResponse>(async () => await sender.Send(new GetMarketPairQuery(symbol), cancellation) is { } pair
            ? MarketResponses.Of(pair)
            : NotFound(symbol));
    }

    /// <summary>
    ///     The ideas of the pair, open and closed, from the newest by default (spec market-overview «Идеи пары»). For the
    ///     chart (task 18.22): <paramref name="from" /> and <paramref name="to" /> — milliseconds since 1970 — give the ideas
    ///     that touch the window, and <paramref name="idea" /> gives the page that idea is on instead of
    ///     <paramref name="page" />.
    /// </summary>
    [HttpGet("pairs/{symbol}/ideas", Name = "ListPairIdeas")]
    [ProducesResponseType<IdeaPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IdeaPageResponse>> Ideas(
        string symbol,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? strategy,
        [FromQuery] string? timeframe,
        [FromQuery] string? side,
        [FromQuery] string? label,
        [FromQuery] string? state,
        [FromQuery] long? from,
        [FromQuery] long? to,
        [FromQuery] Guid? idea,
        CancellationToken cancellation)
    {
        var query = new GetIdeasQuery(
            IdeaListKind.Pair,
            new ListRequest(sort, direction, page, pageSize),
            Symbol: symbol,
            Strategy: strategy,
            Timeframe: timeframe,
            Side: side,
            Label: label,
            State: state,
            From: from is { } start ? DateTimeOffset.FromUnixTimeMilliseconds(start) : null,
            To: to is { } end ? DateTimeOffset.FromUnixTimeMilliseconds(end) : null,
            Around: idea);
        return await Answer<IdeaPageResponse>(async () => IdeaResponses.Of(await sender.Send(query, cancellation)));
    }

    /// <summary>
    ///     A page of 500 closed candles of the timeframe before <paramref name="before" /> — up to now when not given — with the
    ///     open interest and the funding payments of that time (spec market-overview «График пары»).
    /// </summary>
    /// <param name="symbol">The pair.</param>
    /// <param name="timeframe">One of <c>5m</c>, <c>15m</c>, <c>1h</c>, <c>4h</c>, <c>1d</c>.</param>
    /// <param name="before">Milliseconds since 1970: the page ends there.</param>
    /// <param name="cancellation">The request was given up.</param>
    [HttpGet("pairs/{symbol}/chart", Name = "GetPairChart")]
    [ProducesResponseType<PairChartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PairChartResponse>> Chart(string symbol, [FromQuery] string? timeframe, [FromQuery] long? before, CancellationToken cancellation)
    {
        DateTimeOffset? end = before is { } milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : null;
        return await Answer<PairChartResponse>(async () => await sender.Send(new GetPairChartQuery(symbol, timeframe, end), cancellation) is { } chart
            ? MarketResponses.Of(symbol, chart)
            : NotFound(symbol));
    }

    /// <summary>
    ///     The live candle of the pair on the timeframe as <c>text/event-stream</c> (spec market-overview «Живая свеча»): an
    ///     event <c>candle</c> with <see cref="LiveCandleResponse" /> no more than once a second and the closed candle at the
    ///     end of its window, until the page goes. A pair outside the set is 404 before the stream; a failure of the feed ends
    ///     the stream with an event <c>error</c>, and the page connects again (task 18.11).
    /// </summary>
    [HttpGet("pairs/{symbol}/live", Name = "StreamLiveCandle")]
    [Produces("text/event-stream")]
    [ProducesResponseType<LiveCandleResponse>(StatusCodes.Status200OK, "text/event-stream")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task Live(string symbol, [FromQuery] string? timeframe, CancellationToken cancellation)
    {
        if (!Timeframes.TryParse(timeframe, out TimeSpan length))
        {
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            await Response.WriteAsJsonAsync(new ErrorResponse([new RequestError("InvalidTimeframe", $"таймфрейм — один из {Timeframes.Listed}") { Field = "timeframe" }]), cancellation);
            return;
        }

        if (!await live.IsListed(symbol, cancellation))
        {
            await Results.Problem($"{symbol} нет среди торгуемых бессрочных USDT-пар.", statusCode: StatusCodes.Status404NotFound).ExecuteAsync(HttpContext);
            return;
        }

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        await Response.WriteAsync(": live\n\n", cancellation);
        await Response.Body.FlushAsync(cancellation);
        try
        {
            await foreach (LiveCandle candle in live.Listen(symbol, length, cancellation))
            {
                await Response.WriteAsync($"event: candle\ndata: {JsonSerializer.Serialize(MarketResponses.Of(candle), Json)}\n\n", cancellation);
                await Response.Body.FlushAsync(cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The page went.
        }
        catch (Exception exception) when (!cancellation.IsCancellationRequested)
        {
            // The feed failed: the page is told, the stream ends, and the page connects again.
            await Response.WriteAsync($"event: error\ndata: {JsonSerializer.Serialize(new { message = exception.Message }, Json)}\n\n", cancellation);
            await Response.Body.FlushAsync(cancellation);
        }
    }

    private ObjectResult NotFound(string symbol)
    {
        return Problem($"{symbol} нет среди торгуемых бессрочных USDT-пар.", statusCode: StatusCodes.Status404NotFound);
    }

    private async Task<ActionResult<T>> Answer<T>(Func<Task<ActionResult<T>>> act)
    {
        try
        {
            return await act();
        }
        catch (IdeasRequestException exception)
        {
            return UnprocessableEntity(new ErrorResponse([.. exception.Errors.Select(RequestErrors.Of)]));
        }
        catch (HistoryUnavailableException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
