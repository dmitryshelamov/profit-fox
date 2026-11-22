using System.Globalization;
using System.Net;
using System.Text.Json;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Clients.MarketData;

/// <summary>
///     Recent history over the REST of the market-data service (change add-ideas-and-market, design.md, decision 4):
///     <c>GET api/instruments/{id}/candles</c>, <c>GET api/instruments/{id}/funding-payments</c> and
///     <c>GET api/instruments/{id}/open-interest</c>. An answer <c>202</c> says the service is still loading the history of
///     the pair.
/// </summary>
public sealed class RestRecentHistory(HttpClient http) : IRecentHistory
{
    /// <summary>
    ///     The most candles or points one answer gives.
    /// </summary>
    public const int MaxLimit = 1000;

    public async Task<RecentRead<HistoryCandle>> Candles(string instrumentId, TimeSpan interval, DateTimeOffset before, int limit, CancellationToken cancellation)
    {
        string path = $"api/instruments/{Uri.EscapeDataString(instrumentId)}/candles?interval={Interval(interval)}&to={Stamp(before)}&limit={Limit(limit)}";
        return await Read(path, "candles", item => new HistoryCandle(
            Time(item, "openTime"),
            Text(item, "open")!,
            Text(item, "high")!,
            Text(item, "low")!,
            Text(item, "close")!,
            Text(item, "volume")!,
            Text(item, "turnover"),
            Text(item, "buyVolume")), cancellation);
    }

    public async Task<RecentRead<HistoryPayment>> Funding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        string path = $"api/instruments/{Uri.EscapeDataString(instrumentId)}/funding-payments?from={Stamp(from)}&to={Stamp(to)}";
        return await Read(path, "payments", item => new HistoryPayment(Time(item, "time"), Text(item, "fundingRate")!), cancellation);
    }

    public async Task<RecentRead<OpenInterestPoint>> OpenInterest(string instrumentId, TimeSpan interval, DateTimeOffset before, int limit, CancellationToken cancellation)
    {
        string path = $"api/instruments/{Uri.EscapeDataString(instrumentId)}/open-interest?interval={Interval(interval)}&to={Stamp(before)}&limit={Limit(limit)}";
        return await Read(
            path,
            "points",
            item => new OpenInterestPoint(Time(item, "time"), decimal.Parse(Text(item, "openInterest")!, NumberStyles.Float, CultureInfo.InvariantCulture)),
            cancellation);
    }

    /// <summary>
    ///     The name of an interval in the requests of the service: <c>1m</c>, <c>5m</c>, <c>15m</c>, <c>1h</c>, <c>4h</c>,
    ///     <c>1d</c>.
    /// </summary>
    public static string Interval(TimeSpan interval)
    {
        return interval == TimeSpan.FromMinutes(1) ? "1m" : Timeframes.Name(interval);
    }

    private async Task<RecentRead<T>> Read<T>(string path, string property, Func<JsonElement, T> item, CancellationToken cancellation)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(path, cancellation);
        }
        catch (HttpRequestException exception)
        {
            throw new HistoryUnavailableException("The REST of the market-data service did not answer.", exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                return RecentRead<T>.StillLoading;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HistoryUnavailableException($"The REST of the market-data service answered {(int)response.StatusCode} to {path}.");
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            if (document.RootElement.TryGetProperty("loading", out JsonElement loading) && loading.ValueKind == JsonValueKind.True)
            {
                return RecentRead<T>.StillLoading;
            }

            return new RecentRead<T>(Loading: false, [.. document.RootElement.GetProperty(property).EnumerateArray().Select(item)]);
        }
    }

    private static int Limit(int limit)
    {
        return Math.Clamp(limit, 1, MaxLimit);
    }

    private static string Stamp(DateTimeOffset moment)
    {
        return Uri.EscapeDataString(moment.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
    }

    private static DateTimeOffset Time(JsonElement item, string name)
    {
        return DateTimeOffset.Parse(item.GetProperty(name).GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static string? Text(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
