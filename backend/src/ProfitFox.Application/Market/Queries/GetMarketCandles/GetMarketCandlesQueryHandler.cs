using MediatR;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Market.Queries.GetMarketCandles;

public sealed class GetMarketCandlesQueryHandler(CandleReadThrough readThrough, TimeProvider time) : IRequestHandler<GetMarketCandlesQuery, GetMarketCandlesResult>
{
    public async Task<GetMarketCandlesResult> Handle(GetMarketCandlesQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();

        if (!MarketDataCodes.TryParseCategory(request.Category, out MarketCategory category))
        {
            errors["category"] = MarketQueries.InvalidCategory;
        }

        if (!MarketDataCodes.TryParseInterval(category, request.Interval ?? string.Empty, out Interval interval))
        {
            errors["interval"] = MarketDataCodes.InvalidInterval(category);
        }

        (IReadOnlyList<Candle>? candles, bool unavailable) = await ChartCandles.ReadAsync(
            readThrough,
            time,
            new Instrument(category, request.Symbol),
            interval,
            request.From,
            request.To,
            errors,
            cancellationToken);

        return new GetMarketCandlesResult(interval, candles, unavailable, errors);
    }
}