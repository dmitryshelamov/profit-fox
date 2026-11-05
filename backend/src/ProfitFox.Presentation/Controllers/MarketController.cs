using System.Globalization;

using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Ideas.Queries.GetOwnIdea;
using ProfitFox.Application.Ideas.Queries.GetPairIdeas;
using ProfitFox.Application.Ideas.Queries.GetZones;
using ProfitFox.Application.Market.Queries.GetMarket;
using ProfitFox.Application.Market.Queries.GetMarketCandles;
using ProfitFox.Application.Market.Queries.GetMarketPair;
using ProfitFox.Application.Market.Queries.GetMarketSeries;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Presentation.Contracts.Ideas;
using ProfitFox.Presentation.Contracts.Market;

namespace ProfitFox.Presentation.Controllers;

/// <summary>The data of the market the scanner keeps; reads storage only, never Bybit.</summary>
[ApiController]
[Route("api/market")]
public sealed class MarketController(ISender sender) : ControllerBase
{
    /// <summary>One page of the snapshot that meets the filters, in the order asked for, with the counts of the screen and the state of the scanner.</summary>
    [HttpGet]
    [ProducesResponseType<MarketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] MarketQueryParameters parameters, CancellationToken cancellationToken)
    {
        GetMarketResult result = await sender.Send(
            new GetMarketQuery(
                parameters.Category,
                parameters.Search,
                parameters.Setup,
                parameters.Interval,
                parameters.SavedFilter,
                parameters.Sort,
                parameters.Direction,
                parameters.Page,
                parameters.PageSize),
            cancellationToken);

        return result.Page is null ? Invalid(result.Errors) : Ok(MarketResponse.Of(result));
    }

    [HttpGet("{category}/{symbol}")]
    [ProducesResponseType<MarketRowResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pair(string category, string symbol, CancellationToken cancellationToken)
    {
        GetMarketPairResult result = await sender.Send(new GetMarketPairQuery(category, symbol), cancellationToken);

        if (result.Errors.Count > 0)
        {
            return Invalid(result.Errors);
        }

        return result.Row is null ? NotFound() : Ok(MarketRowResponse.Of(result.Row, result.Setups ?? []));
    }

    /// <summary>
    ///     The closed candles of a pair in <c>[from, to)</c> on <c>interval</c>, as the history of the bot chart: what storage lacks is first
    ///     read from Bybit, <c>to</c> is cut to the last closed candle, at most 1000 candles.
    /// </summary>
    [HttpGet("{category}/{symbol}/candles")]
    [ProducesResponseType<MarketCandlesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Candles(string category, string symbol, [FromQuery] MarketCandlesQueryParameters parameters, CancellationToken cancellationToken)
    {
        GetMarketCandlesResult result = await sender.Send(new GetMarketCandlesQuery(category, symbol, parameters.Interval, parameters.From, parameters.To), cancellationToken);

        return result.Candles is null ? Invalid(result.Errors) : Ok(MarketCandlesResponse.Of(result));
    }

    /// <summary>
    ///     The points of open interest of a linear pair in <c>[from, to)</c> in USDT, at the close of the minute before each, from storage; at
    ///     most 1000.
    /// </summary>
    [HttpGet("{category}/{symbol}/open-interest")]
    [ProducesResponseType<IReadOnlyList<OpenInterestPointResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OpenInterest(string category, string symbol, [FromQuery] MarketSeriesQueryParameters parameters, CancellationToken cancellationToken)
    {
        GetMarketSeriesResult result = await sender.Send(new GetMarketSeriesQuery(MarketSeries.OpenInterest, category, symbol, parameters.From, parameters.To), cancellationToken);

        return Series(result, points => points.Select(OpenInterestPointResponse.Of).ToList());
    }

    /// <summary>The funding settlements of a linear pair in <c>[from, to)</c>, from storage; at most 1000.</summary>
    [HttpGet("{category}/{symbol}/funding")]
    [ProducesResponseType<IReadOnlyList<FundingRateResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Funding(string category, string symbol, [FromQuery] MarketSeriesQueryParameters parameters, CancellationToken cancellationToken)
    {
        GetMarketSeriesResult result = await sender.Send(new GetMarketSeriesQuery(MarketSeries.Funding, category, symbol, parameters.From, parameters.To), cancellationToken);

        return Series(result, points => points.Select(FundingRateResponse.Of).ToList());
    }

    /// <summary>The open ideas of a pair and the ones closed in the last 24 hours, by expectancy, highest first.</summary>
    [HttpGet("{category}/{symbol}/ideas")]
    [ProducesResponseType<IReadOnlyList<IdeaResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Ideas(string category, string symbol, CancellationToken cancellationToken)
    {
        GetPairIdeasResult result = await sender.Send(new GetPairIdeasQuery(category, symbol), cancellationToken);

        return result.Ideas is null ? Invalid(result.Errors) : Ok(result.Ideas.Select(IdeaResponse.Of).ToList());
    }

    /// <summary>The zones of support and resistance of a pair on an interval of the setups (5, 15, 60, 240), from its last 200 closed candles.</summary>
    [HttpGet("{category}/{symbol}/zones")]
    [ProducesResponseType<ZonesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Zones(string category, string symbol, [FromQuery] string? interval, CancellationToken cancellationToken)
    {
        GetZonesResult result = await sender.Send(new GetZonesQuery(category, symbol, interval), cancellationToken);

        if (result.NotFound)
        {
            return NotFound();
        }

        return result.Zones is null ? Invalid(result.Errors) : Ok(new ZonesResponse(Text(result.Price!.Value), [.. result.Zones.Select(zone => Zone(zone, result.Price.Value))]));
    }

    /// <summary>An idea of the pair from the price now by the rules of the type, without a setup; it is not stored and has only the baseline.</summary>
    [HttpPost("{category}/{symbol}/own-idea")]
    [ProducesResponseType<OwnIdeaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OwnIdea(string category, string symbol, OwnIdeaBody body, CancellationToken cancellationToken)
    {
        GetOwnIdeaResult result = await sender.Send(new GetOwnIdeaQuery(category, symbol, body.Type, body.Interval, body.Side), cancellationToken);

        if (result.Errors.Count > 0)
        {
            return Invalid(result.Errors);
        }

        if (result.NotFound)
        {
            return NotFound();
        }

        return Ok(result.Plan is { } plan
            ? new OwnIdeaResponse(true, Text(plan.Entry), Text(plan.Stop), Text(plan.Target), Text(plan.RiskReward), Text(result.NetRiskReward!.Value), Text(result.Baseline!.Value), result.Tick?.ToString(CultureInfo.InvariantCulture))
            : new OwnIdeaResponse(false, null, null, null, null, null, null, null));
    }

    private static ZoneResponse Zone(Zone zone, decimal price)
    {
        string kind = zone.High < price ? "support" : zone.Low > price ? "resistance" : "around";

        return new ZoneResponse(Text(zone.Low), Text(zone.High), zone.Strength, kind);
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private IActionResult Series<T>(GetMarketSeriesResult result, Func<IReadOnlyList<(DateTimeOffset Time, decimal Value)>, T> map)
    {
        if (result.NotFound)
        {
            return NotFound();
        }

        return result.Points is null ? Invalid(result.Errors) : Ok(map(result.Points));
    }

    private IActionResult Invalid(IReadOnlyDictionary<string, string> errors)
    {
        return ValidationProblem(new ValidationProblemDetails(errors.ToDictionary(error => error.Key, error => new[] { error.Value })));
    }
}