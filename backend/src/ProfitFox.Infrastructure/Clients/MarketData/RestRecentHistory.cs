using System.Globalization;
using System.Net;
using System.Text.Json;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Clients.MarketData.Protos;

namespace ProfitFox.Infrastructure.Clients.MarketData;

/// <summary>
///     Recent history over the REST of the market-data service (change add-ideas-and-market, design.md, decision 4; the
///     contract of task 18.1):
///     <list type="bullet">
///         <item>
///             <c>GET api/instruments/{id}/candles?interval=&amp;from=&amp;to=</c> — the window <c>[from, to)</c> on the
///             boundaries of the interval in UTC, both moments required, at most 1000 candles, no <c>limit</c>;
///         </item>
///         <item>
///             <c>GET api/instruments/{id}/funding-payments?from=&amp;to=</c> and <c>GET api/instruments/{id}/open-interest?from=&amp;to=</c>
///             — at most 1000 points an answer, read on from <c>continueFrom</c>; the open interest is a point every 5 minutes.
///         </item>
///     </list>
///     The client turns «the newest N candles before a moment» into the aligned window itself. Numbers come as JSON numbers;
///     strings are read too. An answer <c>202</c> or <c>"loading": true</c> says the service is still loading the history
///     of the pair. With a gRPC channel, the open interest of the days before the start of the current UTC day comes from
///     one call of <c>OpenInterestFeed.History</c>, which refuses a window that ends after it (task 18.2), and only the
///     current day from the REST; when History refuses or fails, it is asked again with the end a day earlier and the REST
///     reads the tail (tasks 19.7, 20.7), and when it refuses again the answer is that the history loads, without points
///     and without the REST: the page asks again by its own timer (task 21.2).
/// </summary>
public sealed class RestRecentHistory(HttpClient http, GrpcChannel? channel = null, TimeProvider? time = null, ILogger? logger = null) : IRecentHistory
{
    /// <summary>
    ///     The most candles or points one answer gives.
    /// </summary>
    public const int MaxLimit = 1000;

    // How often the journal tells that History refused the open interest twice, for each reason (task 21.2).
    private static readonly TimeSpan WarningEvery = TimeSpan.FromMinutes(1);

    // When the journal last told each reason of two refusals of History.
    private readonly Dictionary<string, DateTimeOffset> _warned = new(StringComparer.Ordinal);
    private readonly Lock _warnedLock = new();

    public async Task<RecentRead<HistoryCandle>> Candles(string instrumentId, TimeSpan interval, DateTimeOffset before, int limit, CancellationToken cancellation)
    {
        (DateTimeOffset from, DateTimeOffset to) = Window(interval, before, limit);
        string path = $"api/instruments/{Uri.EscapeDataString(instrumentId)}/candles?interval={Interval(interval)}";
        return await Read(path, "candles", from, to, item => new HistoryCandle(
            Time(item, "openTime"),
            Number(item, "open")!,
            Number(item, "high")!,
            Number(item, "low")!,
            Number(item, "close")!,
            Number(item, "volume")!,
            Number(item, "turnover"),
            Number(item, "buyVolume")), cancellation);
    }

    public async Task<RecentRead<HistoryPayment>> Funding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        string path = $"api/instruments/{Uri.EscapeDataString(instrumentId)}/funding-payments";
        return await Read(path, "points", from, to, item => new HistoryPayment(Time(item, "time"), Number(item, "fundingRate")!), cancellation);
    }

    /// <summary>
    ///     The points of 5 minutes of the window of <paramref name="limit" /> intervals before <paramref name="before" />,
    ///     reduced to one per interval: the last point of the interval, at the open of the interval. The days before the
    ///     current UTC day come from <c>OpenInterestFeed.History</c> when there is a channel, the current day from the REST.
    /// </summary>
    public async Task<RecentRead<OpenInterestPoint>> OpenInterest(string instrumentId, TimeSpan interval, DateTimeOffset before, int limit, CancellationToken cancellation)
    {
        (DateTimeOffset from, DateTimeOffset to) = Window(interval, before, limit);
        List<OpenInterestPoint> points = [];
        DateTimeOffset rest = from;
        if (channel is not null)
        {
            DateTimeOffset today = Floor((time ?? TimeProvider.System).GetUtcNow(), TimeSpan.FromDays(1));
            if (from < today)
            {
                // History refused the window — near midnight the clock of the bench may be ahead of that of the service, whose
                // day has not begun yet (task 19.7) — so it is asked again with the end a day earlier, and the REST reads the
                // tail after it (task 20.7); a window no longer than a day after the refusal is read by the REST whole. Refused
                // again, the window is not read from the REST at all — a thousand days of 1d are 288 000 points, and a part
                // of the window given as the whole would hide the rest from the page — and the answer is that it loads: the
                // page asks again by its own timer (task 21.2).
                DateTimeOffset end = to < today ? to : today;
                DateTimeOffset earlier = end - TimeSpan.FromDays(1);
                Stored first = await TryStoredPoints(instrumentId, from, end, cancellation);
                if (first.Points is { } stored)
                {
                    points.AddRange(stored);
                    rest = end;
                }
                else if (earlier > from)
                {
                    Stored second = await TryStoredPoints(instrumentId, from, earlier, cancellation);
                    if (second.Points is not { } older)
                    {
                        WarnRefusedTwice(instrumentId, from, earlier, second.Reason!);
                        return new RecentRead<OpenInterestPoint>(Loading: true, []);
                    }

                    points.AddRange(older);
                    rest = earlier;
                }
            }
        }

        if (rest < to)
        {
            RecentRead<OpenInterestPoint> current = await Points(instrumentId, rest, to, cancellation);
            if (current.Loading)
            {
                return current;
            }

            points.AddRange(current.Items);
        }

        return new RecentRead<OpenInterestPoint>(Loading: false, PerInterval(points, interval));
    }

    // The stored points of [from, to), or the reason History refused or failed.
    private async Task<Stored> TryStoredPoints(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        try
        {
            return new Stored(await StoredPoints(instrumentId, from, to, cancellation), null);
        }
        catch (HistoryUnavailableException exception)
        {
            return new Stored(null, exception.Reason);
        }
    }

    // Two refusals of History go to the journal no more than once a minute for each reason (task 21.2): every page of every
    // pair asks again in 5 seconds while History refuses.
    private void WarnRefusedTwice(string instrumentId, DateTimeOffset from, DateTimeOffset to, string reason)
    {
        if (logger is null)
        {
            return;
        }

        DateTimeOffset now = (time ?? TimeProvider.System).GetUtcNow();
        lock (_warnedLock)
        {
            if (_warned.TryGetValue(reason, out DateTimeOffset last) && now - last < WarningEvery)
            {
                return;
            }

            _warned[reason] = now;
        }

        logger.LogWarning(
            "History refused the open interest of {Instrument} twice, the second time for [{From:O}, {To:O}): {Reason}. The page is answered that it loads and asks again; the same reason is not written again for a minute.",
            instrumentId,
            from,
            to,
            reason);
    }

    // The points History gave, or the reason it refused.
    private sealed record Stored(IReadOnlyList<OpenInterestPoint>? Points, string? Reason);

    /// <summary>
    ///     The stored points of open interest of past days at times in [<paramref name="from" />, <paramref name="to" />)
    ///     from one call of <c>OpenInterestFeed.History</c>, oldest first.
    /// </summary>
    /// <exception cref="HistoryUnavailableException">The service did not answer or refused the window.</exception>
    private async Task<IReadOnlyList<OpenInterestPoint>> StoredPoints(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        var client = new OpenInterestFeed.OpenInterestFeedClient(channel);
        var request = new OpenInterestHistoryRequest { From = Timestamp.FromDateTimeOffset(from), To = Timestamp.FromDateTimeOffset(to) };
        request.InstrumentIds.Add(instrumentId);
        List<OpenInterestPoint> points = [];
        try
        {
            using AsyncServerStreamingCall<OpenInterestHistoryItem> call = client.History(request, cancellationToken: cancellation);
            await foreach (OpenInterestHistoryItem item in call.ResponseStream.ReadAllAsync(cancellation))
            {
                if (item.ItemCase == OpenInterestHistoryItem.ItemOneofCase.Point && item.Point.InstrumentId == instrumentId)
                {
                    points.Add(new OpenInterestPoint(item.Point.Time.ToDateTimeOffset(), decimal.Parse(item.Point.OpenInterest, NumberStyles.Float, CultureInfo.InvariantCulture)));
                }
            }
        }
        catch (RpcException exception) when (exception.StatusCode is not StatusCode.Cancelled)
        {
            string reason = $"The market-data service answered {exception.StatusCode} to the history of open interest";
            throw new HistoryUnavailableException($"{reason}: {exception.Status.Detail}", exception, reason: reason);
        }

        return points;
    }

    /// <summary>
    ///     The points of open interest at times in [<paramref name="from" />, <paramref name="to" />), oldest first, as the
    ///     service keeps them: one every 5 minutes.
    /// </summary>
    public async Task<RecentRead<OpenInterestPoint>> Points(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        string path = $"api/instruments/{Uri.EscapeDataString(instrumentId)}/open-interest";
        return await Read(
            path,
            "points",
            from,
            to,
            item => new OpenInterestPoint(Time(item, "time"), decimal.Parse(Number(item, "openInterest")!, NumberStyles.Float, CultureInfo.InvariantCulture)),
            cancellation);
    }

    /// <summary>
    ///     The last point of each interval, at the open of the interval, oldest first.
    /// </summary>
    public static IReadOnlyList<OpenInterestPoint> PerInterval(IEnumerable<OpenInterestPoint> points, TimeSpan interval)
    {
        var last = new SortedDictionary<DateTimeOffset, OpenInterestPoint>();
        foreach (OpenInterestPoint point in points)
        {
            DateTimeOffset open = Floor(point.Time, interval);
            if (!last.TryGetValue(open, out OpenInterestPoint? held) || held.Time <= point.Time)
            {
                last[open] = point;
            }
        }

        return [.. last.Select(pair => pair.Value with { Time = pair.Key })];
    }

    /// <summary>
    ///     The window of the newest <paramref name="limit" /> (1 to 1000) intervals before <paramref name="before" />: it ends
    ///     at the boundary of the interval not later than <paramref name="before" />.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To) Window(TimeSpan interval, DateTimeOffset before, int limit)
    {
        DateTimeOffset to = Floor(before, interval);
        return (to - (interval * Math.Clamp(limit, 1, MaxLimit)), to);
    }

    /// <summary>
    ///     The name of an interval in the requests of the service: <c>1m</c>, <c>5m</c>, <c>15m</c>, <c>1h</c>, <c>4h</c>,
    ///     <c>1d</c>.
    /// </summary>
    public static string Interval(TimeSpan interval)
    {
        return interval == TimeSpan.FromMinutes(1) ? "1m" : Timeframes.Name(interval);
    }

    // The pages of the window, each from where the one before said to read on.
    private async Task<RecentRead<T>> Read<T>(string path, string property, DateTimeOffset from, DateTimeOffset to, Func<JsonElement, T> item, CancellationToken cancellation)
    {
        List<T> items = [];
        DateTimeOffset start = from;
        while (true)
        {
            string request = $"{path}{(path.Contains('?') ? '&' : '?')}from={Stamp(start)}&to={Stamp(to)}";
            (bool loading, DateTimeOffset? next) = await Page(request, property, items, item, cancellation);
            if (loading)
            {
                return RecentRead<T>.StillLoading;
            }

            if (next is not { } continueFrom || continueFrom >= to || continueFrom <= start)
            {
                return new RecentRead<T>(Loading: false, items);
            }

            start = continueFrom;
        }
    }

    private async Task<(bool Loading, DateTimeOffset? ContinueFrom)> Page<T>(string request, string property, List<T> items, Func<JsonElement, T> item, CancellationToken cancellation)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(request, cancellation);
        }
        catch (HttpRequestException exception)
        {
            throw new HistoryUnavailableException("The REST of the market-data service did not answer.", exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                return (true, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                // The message names what was asked, not the pair: the scanner tells one reason once for all pairs.
                string body = await response.Content.ReadAsStringAsync(cancellation);
                string what = request[..request.IndexOf('?')].Split('/')[^1];
                string reason = $"The REST of the market-data service answered {(int)response.StatusCode} to {what}";
                throw new HistoryUnavailableException($"{reason}: {(body.Length > 200 ? body[..200] : body)}", reason: reason);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("loading", out JsonElement loading) && loading.ValueKind == JsonValueKind.True)
            {
                return (true, null);
            }

            items.AddRange(root.GetProperty(property).EnumerateArray().Select(item));
            return (false, root.TryGetProperty("continueFrom", out JsonElement next) && next.ValueKind == JsonValueKind.String ? Moment(next.GetString()!) : null);
        }
    }

    private static DateTimeOffset Floor(DateTimeOffset moment, TimeSpan interval)
    {
        return new DateTimeOffset(moment.UtcTicks - (moment.UtcTicks % interval.Ticks), TimeSpan.Zero);
    }

    private static string Stamp(DateTimeOffset moment)
    {
        return Uri.EscapeDataString(moment.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
    }

    private static DateTimeOffset Time(JsonElement item, string name)
    {
        return Moment(item.GetProperty(name).GetString()!);
    }

    private static DateTimeOffset Moment(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    // A number as the decimal text the service wrote: a JSON number as it stands, a string as it is, null as nothing.
    private static string? Number(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString(),
            _ => null
        };
    }
}
