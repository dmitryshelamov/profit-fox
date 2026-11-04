using System.Globalization;
using System.Text.Json;

using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Shared.Fixtures.Market;

/// <summary>
///     Reference windows of the market snapshot, all ending at <see cref="End" />. Their values are chosen so that each metric comes out
///     round; the tests write the expected values out.
/// </summary>
public static class MarketFixtures
{
    /// <summary>The end of the last closed minute of every window: 2024-03-10T12:00:00Z.</summary>
    public static readonly DateTimeOffset End = DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture);

    /// <summary>
    ///     1440 minutes before <see cref="End" />, open and close 100 and turnover 1000, except: the first minute opens at 206, the minute
    ///     240 back at 50, 60 back at 125, 15 back at 80; the last one closes at 103; the last 60 minutes have turnover 2000.
    /// </summary>
    public static IReadOnlyList<MarketMinute> Day()
    {
        return Minutes("day");
    }

    /// <summary>
    ///     Only two minutes with trades among the last 10: 10 back (open 200, close 202, turnover 50) and 3 back (open 202, close 212.1,
    ///     turnover 30).
    /// </summary>
    public static IReadOnlyList<MarketMinute> Quiet()
    {
        return Minutes("quiet");
    }

    /// <summary>
    ///     The points of open interest every 5 minutes from 24 hours before <see cref="End" /> to <see cref="End" />, 1800 coins, except 2500 at
    ///     the first, 1600 an hour back and 2000 at <see cref="End" />.
    /// </summary>
    public static IReadOnlyList<OpenInterestPoint> OpenInterest()
    {
        return [.. Rows("open-interest").Select(row => new OpenInterestPoint(Time(row.GetProperty("time")), Number(row.GetProperty("value"))))];
    }

    /// <summary>
    ///     <c>spot</c> / <c>BTCUSDT</c> and <c>linear</c> / <c>SOLUSDT</c>: last price 100.25, bid 99.5, ask 100.5; the linear one with 2000
    ///     coins of open interest and the funding rate 0.0001.
    /// </summary>
    public static MarketTicker Ticker(MarketCategory category)
    {
        JsonElement row = Rows("tickers").Single(ticker => ticker.GetProperty("category").GetString() == (category == MarketCategory.Spot ? "spot" : "linear"));

        return new MarketTicker(
            new Instrument(category, row.GetProperty("symbol").GetString()!),
            Number(row.GetProperty("last")),
            Number(row.GetProperty("bid")),
            Number(row.GetProperty("ask")),
            row.TryGetProperty("openInterest", out JsonElement openInterest) ? Number(openInterest) : null,
            row.TryGetProperty("fundingRate", out JsonElement fundingRate) ? Number(fundingRate) : null);
    }

    private static IReadOnlyList<MarketMinute> Minutes(string name)
    {
        return
        [
            .. Rows(name).Select(row => new MarketMinute(
                Time(row.GetProperty("openTime")),
                Number(row.GetProperty("open")),
                Number(row.GetProperty("close")),
                Number(row.GetProperty("turnover"))))
        ];
    }

    private static List<JsonElement> Rows(string name)
    {
        using Stream stream = typeof(MarketFixtures).Assembly.GetManifestResourceStream($"ProfitFox.Tests.Shared.Fixtures.Market.{name}.json")!;
        using var document = JsonDocument.Parse(stream);

        return [.. document.RootElement.EnumerateArray().Select(row => row.Clone())];
    }

    private static DateTimeOffset Time(JsonElement value)
    {
        return DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    private static decimal Number(JsonElement value)
    {
        return decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture);
    }
}