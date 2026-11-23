using System.Globalization;

using Microsoft.AspNetCore.Http;

namespace ProfitFox.Tests.Integration.MarketData;

/// <summary>
///     The REST of recent history of the fake market-data service, as the real service answers it (change
///     add-ideas-and-market, task 18.1):
///     <list type="bullet">
///         <item>
///             <c>GET api/instruments/{id}/candles?interval=&amp;from=&amp;to=</c> — both moments required, the window
///             <c>[from, to)</c> on the boundaries of the interval in UTC, at most 1000 candles, no <c>limit</c>; a
///             <c>to</c> in the future is allowed and gives the closed candles only. The answer
///             <c>{ "loading": false, "candles": [...], "covered": [...] }</c> carries the numbers as JSON numbers.
///         </item>
///         <item>
///             <c>GET api/instruments/{id}/funding-payments?from=&amp;to=</c> and <c>GET api/instruments/{id}/open-interest?from=&amp;to=</c>
///             — <c>{ "points": [...], "coveredFrom", "coveredUntil", "continueFrom" }</c>, at most 1000 points an answer,
///             <c>continueFrom</c> where to read on; the open interest is a point every 5 minutes, without <c>interval</c>.
///         </item>
///         <item>A pair of <see cref="FakeMarketData.Loading" /> is still loading: <c>202</c> with <c>"loading": true</c> and an empty list.</item>
///         <item>A request without <c>from</c> or <c>to</c>, with a window off the boundaries or with more than 1000 candles: <c>400</c>.</item>
///     </list>
///     The prices are those of the fake from <see cref="FakeMarketData.CoveredFrom" />, the current day included, the payments
///     those of <see cref="FakeMarketData.RateAt" />, the open interest 24 000 coins.
/// </summary>
internal static class FakeRecentHistory
{
    /// <summary>
    ///     The most candles or points one answer gives.
    /// </summary>
    public const int MaxItems = 1000;

    private static readonly TimeSpan InterestStep = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FundingStep = TimeSpan.FromHours(8);

    public static IResult Candles(FakeMarketData data, HttpRequest request, string id)
    {
        string? interval = request.Query["interval"];
        Record(data, request, $"candles {id} {interval}");
        if (Step(interval) is not { } step)
        {
            return BadRequest($"interval must be one of 1m, 5m, 15m, 1h, 4h, 1d, not '{interval}'");
        }

        if (Window(request) is not ({ } from, { } to))
        {
            return BadRequest("from and to are required");
        }

        if (from >= to)
        {
            return BadRequest("from must be before to");
        }

        if (Floor(from, step) != from || Floor(to, step) != to)
        {
            return BadRequest($"from and to must be on the boundaries of {interval}");
        }

        if ((to - from) / step > MaxItems)
        {
            return BadRequest($"at most {MaxItems} candles a request");
        }

        if (data.Loading.Contains(id))
        {
            return Results.Json(new { loading = true, candles = Array.Empty<object>(), covered = Array.Empty<object>() }, statusCode: StatusCodes.Status202Accepted);
        }

        DateTimeOffset now = data.Time.GetUtcNow();
        decimal close = decimal.Parse(data.Closes.GetValueOrDefault(id, "100.25"), CultureInfo.InvariantCulture);
        List<object> candles = [];
        for (DateTimeOffset open = Max(from, FakeMarketData.CoveredFrom); open < to && open + step <= now; open += step)
        {
            decimal volume = step == TimeSpan.FromMinutes(1) ? (open.Hour == 8 ? 0 : 12.5m) : 12.5m * (decimal)step.TotalMinutes;
            candles.Add(new
            {
                openTime = Text(open),
                open = 100.5m,
                high = 101m,
                low = 100m,
                close,
                volume,
                turnover = volume * close,
                buyVolume = (decimal?)null
            });
        }

        object[] covered = candles.Count > 0 ? [new { from = Text(Max(from, FakeMarketData.CoveredFrom)), to = Text(Min(to, Floor(now, step))) }] : [];
        return Results.Json(new { loading = false, candles, covered });
    }

    public static IResult Funding(FakeMarketData data, HttpRequest request, string id)
    {
        Record(data, request, $"funding {id}");
        return Points(
            data,
            request,
            id,
            FundingStep,
            time => new { time = Text(time), fundingRate = decimal.Parse(FakeMarketData.RateAt(time), CultureInfo.InvariantCulture) });
    }

    public static IResult OpenInterest(FakeMarketData data, HttpRequest request, string id)
    {
        Record(data, request, $"open-interest {id}");
        return Points(data, request, id, InterestStep, time => new { time = Text(time), openInterest = 24_000m });
    }

    // A page of points every step from the coverage, with where to read on when more than a page is in the window.
    private static IResult Points(FakeMarketData data, HttpRequest request, string id, TimeSpan step, Func<DateTimeOffset, object> point)
    {
        if (Window(request) is not ({ } from, { } to))
        {
            return BadRequest("from and to are required");
        }

        if (from >= to)
        {
            return BadRequest("from must be before to");
        }

        if (data.Loading.Contains(id))
        {
            return Results.Json(new { loading = true, points = Array.Empty<object>() }, statusCode: StatusCodes.Status202Accepted);
        }

        DateTimeOffset now = data.Time.GetUtcNow();
        DateTimeOffset until = Min(to, now);
        List<object> points = [];
        DateTimeOffset time = Ceiling(Max(from, FakeMarketData.CoveredFrom), step);
        for (; time < until && points.Count < MaxItems; time += step)
        {
            points.Add(point(time));
        }

        string? continueFrom = time < until ? Text(time) : null;
        return Results.Json(new
        {
            points,
            coveredFrom = Text(FakeMarketData.CoveredFrom),
            coveredUntil = Text(Floor(now, step)),
            continueFrom
        });
    }

    private static void Record(FakeMarketData data, HttpRequest request, string name)
    {
        lock (data.RestRequests)
        {
            data.RestRequests.Add(name);
            data.RestQueries.Add($"{request.Path}{request.QueryString}");
        }
    }

    private static (DateTimeOffset? From, DateTimeOffset? To) Window(HttpRequest request)
    {
        return (Moment(request.Query["from"]), Moment(request.Query["to"]));
    }

    private static DateTimeOffset? Moment(string? text)
    {
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset moment) ? moment : null;
    }

    private static IResult BadRequest(string message)
    {
        return Results.Json(new { error = message }, statusCode: StatusCodes.Status400BadRequest);
    }

    private static TimeSpan? Step(string? interval)
    {
        return interval switch
        {
            "1m" => TimeSpan.FromMinutes(1),
            "5m" => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "1h" => TimeSpan.FromHours(1),
            "4h" => TimeSpan.FromHours(4),
            "1d" => TimeSpan.FromDays(1),
            _ => null
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

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
    {
        return left > right ? left : right;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left < right ? left : right;
    }

    private static string Text(DateTimeOffset moment)
    {
        return moment.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss+00:00", CultureInfo.InvariantCulture);
    }
}
