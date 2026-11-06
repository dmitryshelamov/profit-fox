using System.Globalization;
using System.Text.Json;

using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Tests.Shared.Fixtures.Ideas;

/// <summary>Reference candle sets of the trade ideas; the tests write the precomputed answers out.</summary>
public static class IdeaFixtures
{
    /// <summary>30 hourly candles from 2024-03-01T00:00:00Z, every fifth from the fourth opening with a gap of 2.5 from the close before it.</summary>
    public static IReadOnlyList<IdeaCandle> Atr()
    {
        return Candles("atr");
    }

    /// <summary>120 hourly candles from 2024-03-01T00:00:00Z, a random walk from 100.00 that closes at most 1.60 from the close before.</summary>
    public static IReadOnlyList<IdeaCandle> Rsi()
    {
        return Candles("rsi");
    }

    /// <summary>
    ///     The minutes of 2024-03-01 from 10:00 to 12:00 that had trades, from 50.00: none at 10:20–10:24 (a whole 5 minute candle),
    ///     11:00–11:02 (the start of an hour) and 11:58–11:59; the price stays through each gap, the minutes 10:25 and 11:03 open 0.90 above
    ///     it.
    /// </summary>
    public static IReadOnlyList<IdeaCandle> Minutes()
    {
        return Candles("minutes");
    }

    /// <summary>
    ///     Minutes from 2024-02-29T23:30:00Z to 2024-03-01T05:10:00Z for the 15 minute setups. Before the move every 15 minutes is a candle
    ///     of 100 with a low of 99 at its first minute, a high of 101 at its last and 100 of volume a minute, so ATR(14) is 2 and the median
    ///     volume 1500:
    ///     <list type="bullet">
    ///         <item><c>impulse</c>: from 03:30 up 0.05 a minute with 200 of volume, from 04:45 up 0.22 with 300, to 107.05 at 05:00 and on;</item>
    ///         <item><c>early</c>: flat until 04:45, then 15 minutes up 0.22 with 300, flat again from 05:00;</item>
    ///         <item><c>drop</c> and <c>early-drop</c>: the same mirrored around 100.</item>
    ///     </list>
    /// </summary>
    public static IReadOnlyList<IdeaCandle> Setup(string name)
    {
        return Candles(name);
    }

    /// <summary>
    ///     Minutes of 2024-03-01 from 00:00 to 08:00 for a 15 minute breakout. Until 07:30 every 15 minutes is a candle of 100 with a low of 99 at
    ///     its first minute, a high of 101 at its last and 100 of volume a minute; the candles from 01:15, 03:00 and 04:45 reach 101.6, 101.8
    ///     and 101.7, the one resistance at 101.6–101.8. From 07:30 the price climbs with 300 a minute, closes 102.1 at 07:33 and stays at
    ///     102.1–102.3 to 07:42; from 07:43 it stands at 101.9 with 100 a minute.
    /// </summary>
    public static IReadOnlyList<IdeaCandle> Breakout()
    {
        return Candles("breakout");
    }

    /// <summary>
    ///     The breakout set with a second zone just below the close: the candles from 04:45 and 06:00 reach 103.0 and 103.05 instead, so the
    ///     resistances are 101.6–101.8 and 103.0–103.05; ATR(14) of the candles up to 07:15 is 2.21509…. The price climbs to 103.1 at 07:33,
    ///     breaks the lower zone with the margin, stays at 103.1 to 07:42, and from 07:43 stands at 101.9.
    /// </summary>
    public static IReadOnlyList<IdeaCandle> BreakoutUnderZone()
    {
        return Candles("breakout-two-zones");
    }

    /// <summary>
    ///     Hourly candles for the zones: <c>two-supports</c>, 200 candles from 103 to 104 with single dips to a low of 100.0, 100.2, 100.4,
    ///     100.1 and 95 (candles 20, 60, 100, 140 and 180); <c>zones</c>, a random walk of 230 candles from 50.00 that ends at 57.55.
    /// </summary>
    public static IReadOnlyList<IdeaCandle> Zones(string name)
    {
        return Candles(name);
    }

    public static IReadOnlyList<IdeaCandle> Candles(string name)
    {
        return [.. Rows(name).Select(Candle)];
    }

    public static List<JsonElement> Rows(string name)
    {
        using Stream stream = typeof(IdeaFixtures).Assembly.GetManifestResourceStream($"ProfitFox.Tests.Shared.Fixtures.Ideas.{name}.json")!;
        using var document = JsonDocument.Parse(stream);

        return [.. document.RootElement.EnumerateArray().Select(row => row.Clone())];
    }

    public static IdeaCandle Candle(JsonElement row)
    {
        return new IdeaCandle(
            Time(row.GetProperty("openTime")),
            Number(row.GetProperty("open")),
            Number(row.GetProperty("high")),
            Number(row.GetProperty("low")),
            Number(row.GetProperty("close")),
            Number(row.GetProperty("volume")));
    }

    public static DateTimeOffset Time(JsonElement value)
    {
        return DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    public static decimal Number(JsonElement value)
    {
        return decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture);
    }
}