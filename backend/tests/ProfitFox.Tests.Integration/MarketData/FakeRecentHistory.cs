using System.Globalization;

using Microsoft.AspNetCore.Http;

namespace ProfitFox.Tests.Integration.MarketData;

/// <summary>
///     The REST of recent history of the fake market-data service (change add-ideas-and-market, task 5.4): candles of any
///     interval with the prices of the fake from <see cref="FakeMarketData.CoveredFrom" />, the current day included, the
///     payments of <see cref="FakeMarketData.RateAt" /> and a point of open interest of 24 000 coins per interval. A pair of
///     <see cref="FakeMarketData.Loading" /> is still loading: it answers <c>202</c>.
/// </summary>
internal static class FakeRecentHistory
{
    public static IResult Candles(FakeMarketData data, string id, string interval, DateTimeOffset to, int limit)
    {
        if (Record(data, $"candles {id} {interval}") is { } loading)
        {
            return loading;
        }

        TimeSpan step = Step(interval);
        DateTimeOffset last = Floor(to, step) - step;
        List<object> candles = [];
        for (DateTimeOffset open = last; candles.Count < limit && open >= FakeMarketData.CoveredFrom; open -= step)
        {
            string close = data.Closes.GetValueOrDefault(id, "100.25");
            decimal volume = step == TimeSpan.FromMinutes(1) ? (open.Hour == 8 ? 0 : 12.5m) : 12.5m * (decimal)step.TotalMinutes;
            candles.Insert(0, new
            {
                openTime = Text(open),
                open = "100.5",
                high = "101",
                low = "100",
                close,
                volume = volume.ToString(CultureInfo.InvariantCulture),
                turnover = (string?)null,
                buyVolume = "6"
            });
        }

        return Results.Json(new { loading = false, candles });
    }

    public static IResult Funding(FakeMarketData data, string id, DateTimeOffset from, DateTimeOffset to)
    {
        if (Record(data, $"funding {id}") is { } loading)
        {
            return loading;
        }

        List<object> payments = [];
        DateTimeOffset first = from > FakeMarketData.CoveredFrom ? Ceiling(from, TimeSpan.FromHours(8)) : FakeMarketData.CoveredFrom;
        for (DateTimeOffset time = first; time < to; time += TimeSpan.FromHours(8))
        {
            payments.Add(new { time = Text(time), fundingRate = FakeMarketData.RateAt(time) });
        }

        return Results.Json(new { loading = false, payments });
    }

    public static IResult OpenInterest(FakeMarketData data, string id, string interval, DateTimeOffset to, int limit)
    {
        if (Record(data, $"open-interest {id} {interval}") is { } loading)
        {
            return loading;
        }

        TimeSpan step = Step(interval);
        List<object> points = [];
        for (DateTimeOffset time = Floor(to, step) - step; points.Count < limit && time >= FakeMarketData.CoveredFrom; time -= step)
        {
            points.Insert(0, new { time = Text(time), openInterest = "24000" });
        }

        return Results.Json(new { loading = false, points });
    }

    private static IResult? Record(FakeMarketData data, string request)
    {
        lock (data.RestRequests)
        {
            data.RestRequests.Add(request);
        }

        string id = request.Split(' ')[1];
        return data.Loading.Contains(id) ? Results.Json(new { loading = true }, statusCode: StatusCodes.Status202Accepted) : null;
    }

    private static TimeSpan Step(string interval)
    {
        return interval switch
        {
            "1m" => TimeSpan.FromMinutes(1),
            "5m" => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "1h" => TimeSpan.FromHours(1),
            "4h" => TimeSpan.FromHours(4),
            "1d" => TimeSpan.FromDays(1),
            _ => throw new BadHttpRequestException($"no interval {interval}")
        };
    }

    private static DateTimeOffset Floor(DateTimeOffset moment, TimeSpan step)
    {
        return new DateTimeOffset(moment.UtcTicks - (moment.UtcTicks % step.Ticks), TimeSpan.Zero);
    }

    private static DateTimeOffset Ceiling(DateTimeOffset moment, TimeSpan step)
    {
        DateTimeOffset floor = Floor(moment, step);
        return floor == moment ? floor : floor + step;
    }

    private static string Text(DateTimeOffset moment)
    {
        return moment.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }
}
