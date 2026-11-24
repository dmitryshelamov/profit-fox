using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Filters;
using ProfitFox.Application.Queries.Ideas;
using ProfitFox.Domain.ValueObjects.Lists.Enums;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Errors.Converters;
using ProfitFox.Presentation.Contracts.Ideas;
using ProfitFox.Presentation.Contracts.Ideas.Converters;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>/api/ideas</c> (change add-ideas-and-market, spec strategy-ideas): the open and the closed ideas a page at a time,
///     sorted by any column and chosen above the table, the summary of the journal and the state of the live data. A field of
///     sorting outside the list, a wrong page or a filter of «Рынок» answers 422 with the place of the error.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class IdeasController(ISender sender) : ControllerBase
{
    /// <param name="state"><c>open</c> (by default) or <c>closed</c>.</param>
    /// <param name="sort">A column of the list: by the moment, or the exit for closed ideas, from the newest by default.</param>
    /// <param name="direction"><c>asc</c> or <c>desc</c>.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">50, 100 or 200.</param>
    /// <param name="search">A part of the symbol.</param>
    /// <param name="strategy">The name of a strategy.</param>
    /// <param name="timeframe">A timeframe.</param>
    /// <param name="side"><c>long</c> or <c>short</c>.</param>
    /// <param name="label">The text of a label.</param>
    /// <param name="positive">Only open ideas in the plus now.</param>
    /// <param name="withStop">Only open ideas with a stop.</param>
    /// <param name="exitReason">Closed ideas closed by <c>stop</c>, <c>target</c> or <c>time</c>.</param>
    /// <param name="period">Closed ideas with the exit in the last days.</param>
    /// <param name="filter">A saved filter of «Идеи».</param>
    /// <param name="cancellation">The request was given up.</param>
    [HttpGet(Name = "ListIdeas")]
    [ProducesResponseType<IdeaPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IdeaPageResponse>> List(
        [FromQuery] string? state,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] string? strategy,
        [FromQuery] string? timeframe,
        [FromQuery] string? side,
        [FromQuery] string? label,
        [FromQuery] bool? positive,
        [FromQuery] bool? withStop,
        [FromQuery] string? exitReason,
        [FromQuery] int? period,
        [FromQuery] Guid? filter,
        CancellationToken cancellation)
    {
        GetIdeasQuery query = Query(state, sort, direction, page, pageSize, search, strategy, timeframe, side, label, positive, withStop, exitReason, period, filter);
        return await Answer(async () => IdeaResponses.Of(await sender.Send(query, cancellation)));
    }

    /// <summary>
    ///     The summary of the closed ideas by strategy, version and timeframe, with the choices of the journal; sorted by the
    ///     number of ideas by default.
    /// </summary>
    [HttpGet("summary", Name = "SummarizeIdeas")]
    [ProducesResponseType<IdeaSummaryPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IdeaSummaryPageResponse>> Summary(
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] string? strategy,
        [FromQuery] string? timeframe,
        [FromQuery] string? side,
        [FromQuery] string? label,
        [FromQuery] string? exitReason,
        [FromQuery] int? period,
        [FromQuery] Guid? filter,
        CancellationToken cancellation)
    {
        GetIdeasQuery query = Query("closed", sort, direction, page, pageSize, search, strategy, timeframe, side, label, null, null, exitReason, period, filter);
        return await Answer(async () => IdeaResponses.Of(await sender.Send(new GetIdeaSummaryQuery(query), cancellation)));
    }

    /// <summary>
    ///     The state of the live data: who keeps it, since when ideas count without a break and how many pairs are warmed.
    /// </summary>
    [HttpGet("state", Name = "GetScannerState")]
    [ProducesResponseType<ScannerStateResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ScannerStateResponse>> State(CancellationToken cancellation)
    {
        return IdeaResponses.Of(await sender.Send(new GetScannerStateQuery(), cancellation));
    }

    private static GetIdeasQuery Query(
        string? state,
        string? sort,
        string? direction,
        int? page,
        int? pageSize,
        string? search,
        string? strategy,
        string? timeframe,
        string? side,
        string? label,
        bool? positive,
        bool? withStop,
        string? exitReason,
        int? period,
        Guid? filter)
    {
        return new GetIdeasQuery(
            state == "closed" ? IdeaListKind.Closed : IdeaListKind.Open,
            new ListRequest(sort, direction, page, pageSize, filter),
            Search: search,
            Strategy: strategy,
            Timeframe: timeframe,
            Side: side,
            Label: label,
            OnlyPositive: positive == true,
            OnlyWithStop: withStop == true,
            ExitReason: exitReason,
            PeriodDays: period);
    }

    private async Task<ActionResult<T>> Answer<T>(Func<Task<T>> act)
    {
        try
        {
            return await act();
        }
        catch (IdeasRequestException exception)
        {
            return UnprocessableEntity(new ErrorResponse([.. exception.Errors.Select(RequestErrors.Of)]));
        }
    }
}
