using MediatR;

using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.MarketData.Queries.GetCandles;

public sealed class GetCandlesQueryHandler(ICandleRepository candles) : IRequestHandler<GetCandlesQuery, GetCandlesResult>
{
    public const int DefaultLimit = 1000;
    public const int MaxLimit = 5000;

    public async Task<GetCandlesResult> Handle(GetCandlesQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        if (!MarketDataCodes.TryParseCategory(request.Category, out MarketCategory category))
        {
            errors["category"] = "Category must be spot or linear.";
        }

        if (!MarketDataCodes.TryParseInterval(request.Interval, out Interval interval))
        {
            errors["interval"] = "Interval must be one of Bybit intervals: 1, 3, 5, 15, 30, 60, 120, 240, 360, 720, D, W, M.";
        }

        if (!ApiTimestamps.TryParse(request.From, out DateTimeOffset from))
        {
            errors["from"] = ApiTimestamps.Invalid("from");
        }

        bool toIsTime = ApiTimestamps.TryParse(request.To, out DateTimeOffset to);
        if (!toIsTime)
        {
            errors["to"] = ApiTimestamps.Invalid("to");
        }

        // An unparsed `from` stays at its minimum and never fails the period check.
        if (toIsTime && from >= to)
        {
            errors["from"] = "from must be earlier than to.";
        }

        int limit = request.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaxLimit)
        {
            errors["limit"] = $"limit must be from 1 to {MaxLimit}.";
        }

        if (errors.Count > 0)
        {
            return new GetCandlesResult(Candles: null, errors);
        }

        IReadOnlyList<Candle> found = await candles.GetAsync(
            new Instrument(category, request.Symbol), interval, from.ToUniversalTime(), to.ToUniversalTime(), limit, cancellationToken);
        return new GetCandlesResult(found, errors);
    }
}
