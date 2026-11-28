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
    /// <param name="kind">The kind of the pair of the idea: <c>crypto</c> or <c>stock</c>; every kind when not given.</param>
    /// <param name="minScore">Only ideas with a score of at least this number, with at most two digits after the point.</param>
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
        [FromQuery] string? kind,
        [FromQuery] string? minScore,
        CancellationToken cancellation)
    {
        GetIdeasQuery query = Query(state, sort, direction, page, pageSize, search, strategy, timeframe, side, label, positive, withStop, exitReason, period, filter, kind, minScore);
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
        [FromQuery] string? kind,
        [FromQuery] string? minScore,
        CancellationToken cancellation)
    {
        GetIdeasQuery query = Query("closed", sort, direction, page, pageSize, search, strategy, timeframe, side, label, null, null, exitReason, period, filter, kind, minScore);
        return await Answer(async () => IdeaResponses.Of(await sender.Send(new GetIdeaSummaryQuery(query), cancellation)));
    }

    /// <summary>
    ///     The outcomes of the closed ideas by their score now (change add-idea-score, spec idea-score «Итоги по баллу»): a row
    ///     a group of scores half a point wide with at least one idea, from the highest (change decimal-label-weights), with
    ///     the choices, the period and the saved filter of the journal; excluded ideas do not count. A choice outside its list
    ///     or a least score that is not a number with at most two digits after the point answers 422.
    /// </summary>
    /// <param name="search">A part of the symbol.</param>
    /// <param name="strategy">The name of a strategy.</param>
    /// <param name="timeframe">A timeframe.</param>
    /// <param name="side"><c>long</c> or <c>short</c>.</param>
    /// <param name="label">The text of a label.</param>
    /// <param name="exitReason">Closed ideas closed by <c>stop</c>, <c>target</c> or <c>time</c>.</param>
    /// <param name="period">Closed ideas with the exit in the last days.</param>
    /// <param name="filter">A saved filter of «Идеи».</param>
    /// <param name="kind">The kind of the pair of the idea: <c>crypto</c> or <c>stock</c>; every kind when not given.</param>
    /// <param name="minScore">Only ideas with a score of at least this number, with at most two digits after the point.</param>
    /// <param name="cancellation">The request was given up.</param>
    [HttpGet("scores", Name = "ScoreIdeas")]
    [ProducesResponseType<IdeaScoresResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IdeaScoresResponse>> Scores(
        [FromQuery] string? search,
        [FromQuery] string? strategy,
        [FromQuery] string? timeframe,
        [FromQuery] string? side,
        [FromQuery] string? label,
        [FromQuery] string? exitReason,
        [FromQuery] int? period,
        [FromQuery] Guid? filter,
        [FromQuery] string? kind,
        [FromQuery] string? minScore,
        CancellationToken cancellation)
    {
        GetIdeasQuery query = Query("closed", null, null, null, null, search, strategy, timeframe, side, label, null, null, exitReason, period, filter, kind, minScore);
        return await Answer(async () => IdeaResponses.Of(await sender.Send(new GetIdeaScoresQuery(query), cancellation)));
    }

    /// <summary>
    ///     How many open or closed ideas each strategy has (change add-setup-strategies, spec strategy-ideas «Число идей по
    ///     стратегиям»): with the choices, the period and the saved filter of the list, but not its strategy — the row of
    ///     strategies above the table counts each one with the others chosen. A choice outside its list answers 422.
    /// </summary>
    /// <param name="state"><c>open</c> (by default) or <c>closed</c>.</param>
    /// <param name="search">A part of the symbol.</param>
    /// <param name="strategy">The strategy of the list; it does not narrow the count.</param>
    /// <param name="timeframe">A timeframe.</param>
    /// <param name="side"><c>long</c> or <c>short</c>.</param>
    /// <param name="label">The text of a label.</param>
    /// <param name="positive">Only open ideas in the plus now.</param>
    /// <param name="withStop">Only open ideas with a stop.</param>
    /// <param name="exitReason">Closed ideas closed by <c>stop</c>, <c>target</c> or <c>time</c>.</param>
    /// <param name="period">Closed ideas with the exit in the last days.</param>
    /// <param name="filter">A saved filter of «Идеи».</param>
    /// <param name="kind">The kind of the pair of the idea: <c>crypto</c> or <c>stock</c>; every kind when not given.</param>
    /// <param name="minScore">Only ideas with a score of at least this number, with at most two digits after the point.</param>
    /// <param name="cancellation">The request was given up.</param>
    [HttpGet("strategies", Name = "CountIdeasByStrategy")]
    [ProducesResponseType<IdeaStrategyCountsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IdeaStrategyCountsResponse>> Strategies(
        [FromQuery] string? state,
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
        [FromQuery] string? kind,
        [FromQuery] string? minScore,
        CancellationToken cancellation)
    {
        _ = strategy;
        var query = new GetIdeaStrategiesQuery(state, search, timeframe, side, label, positive == true, withStop == true, exitReason, period, filter, kind, minScore);
        return await Answer(async () => IdeaResponses.Of(await sender.Send(query, cancellation)));
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
        Guid? filter,
        string? kind,
        string? minScore)
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
            PeriodDays: period,
            PairKind: kind,
            MinScore: minScore);
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
