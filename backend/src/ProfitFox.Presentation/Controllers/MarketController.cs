using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Market.Queries.GetMarket;
using ProfitFox.Application.Market.Queries.GetMarketCandles;
using ProfitFox.Application.Market.Queries.GetMarketPair;
using ProfitFox.Application.Market.Queries.GetMarketSeries;
using ProfitFox.Presentation.Contracts.Market;

namespace ProfitFox.Presentation.Controllers;

/// <summary>The data of the market the scanner keeps; reads storage only, never Bybit.</summary>
[ApiController]
[Route("api/market")]
public sealed class MarketController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MarketResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        return Ok(MarketResponse.Of(await sender.Send(new GetMarketQuery(), cancellationToken)));
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

        return result.Row is null ? NotFound() : Ok(MarketRowResponse.Of(result.Row));
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