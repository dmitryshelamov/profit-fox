using MediatR;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Market.Queries.GetMarketSeries;

public sealed class GetMarketSeriesQueryHandler(IOpenInterestRepository openInterest, IFundingRateRepository funding, ICandleRepository candles)
    : IRequestHandler<GetMarketSeriesQuery, GetMarketSeriesResult>
{
    private const int MaxPoints = 1000;

    private static readonly TimeSpan PriceLookBack = TimeSpan.FromDays(1);

    public async Task<GetMarketSeriesResult> Handle(GetMarketSeriesQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();

        if (!MarketDataCodes.TryParseCategory(request.Category, out MarketCategory category))
        {
            errors["category"] = MarketQueries.InvalidCategory;
        }
        else if (category == MarketCategory.Spot)
        {
            return new GetMarketSeriesResult(null, NotFound: true, errors);
        }

        if (!ApiTimestamps.TryParse(request.From, out DateTimeOffset from))
        {
            errors["from"] = ApiTimestamps.Invalid("from");
        }

        if (!ApiTimestamps.TryParse(request.To, out DateTimeOffset to))
        {
            errors["to"] = ApiTimestamps.Invalid("to");
        }

        if (errors.Count > 0)
        {
            return new GetMarketSeriesResult(null, NotFound: false, errors);
        }

        var instrument = new Instrument(category, request.Symbol);

        // One more than the limit tells a period that is too long.
        IReadOnlyList<(DateTimeOffset Time, decimal Value)> points = request.Series == MarketSeries.OpenInterest
            ? [.. (await openInterest.GetAsync(instrument, from, to, MaxPoints + 1, cancellationToken)).Select(point => (point.Time, point.Value))]
            : [.. (await funding.GetAsync(instrument, from, to, MaxPoints + 1, cancellationToken)).Select(rate => (rate.Time, rate.Rate))];

        if (points.Count > MaxPoints)
        {
            errors["from"] = $"The period must not have more than {MaxPoints} points.";

            return new GetMarketSeriesResult(null, NotFound: false, errors);
        }

        return new GetMarketSeriesResult(request.Series == MarketSeries.OpenInterest ? await InUsdtAsync(instrument, points, from, to, cancellationToken) : points, NotFound: false, errors);
    }

    /// <summary>
    ///     Open interest is stored in coins (decision 6) and read in USDT: a point is worth its coins at the close of the last minute before
    ///     it, a minute without trades keeping the close before it; a point with no minute in the day before it is left out.
    /// </summary>
    private async Task<IReadOnlyList<(DateTimeOffset Time, decimal Value)>> InUsdtAsync(
        Instrument instrument,
        IReadOnlyList<(DateTimeOffset Time, decimal Value)> points,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        DateTimeOffset since = from - PriceLookBack;
        IReadOnlyList<Candle> minutes = await candles.GetAsync(instrument, Interval.OneMinute, since, to, (int)Math.Ceiling((to - since).TotalMinutes), cancellationToken);
        var worth = new List<(DateTimeOffset Time, decimal Value)>();
        var next = 0;
        decimal? close = null;

        foreach ((DateTimeOffset time, decimal coins) in points)
        {
            while (next < minutes.Count && minutes[next].OpenTime < time)
            {
                close = minutes[next++].Close.Value;
            }

            if (close is { } price)
            {
                worth.Add((time, coins * price));
            }
        }

        return worth;
    }
}