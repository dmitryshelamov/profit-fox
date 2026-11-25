using ProfitFox.Domain.ValueObjects.Execution;

namespace ProfitFox.Tests.Unit.Domain.Services.Nodes;

/// <summary>
///     Change add-setup-strategies, task 2.7: <c>zones</c> by the rules of <c>ZoneFinder</c> of v2 (spec strategy-execution
///     «Зоны поддержки и сопротивления», «Края ближайших зон»), and on random candles the edges of the zones found by a
///     plain count after <c>ZoneFinder.Find</c>: touches of the window, sorted, merged from the bottom up.
/// </summary>
public sealed class ZonesNodeTests
{
    // Highs with touches at 100.0 (third candle) and 100.3 (seventh); the lows are flat and give no touch.
    private static readonly decimal[] TwoTouches = [99, 99.5m, 100.0m, 99.5m, 99, 99.5m, 100.3m, 99.5m, 99, 99];

    [Test]
    public void Two_touches_close_together_are_a_zone()
    {
        (decimal? above, decimal? below) = Edges(TwoTouches, price: 100.1m);

        Assert.That((above, below), Is.EqualTo(((decimal?)100.3m, (decimal?)100.0m)));
    }

    [Test]
    public void A_price_under_the_zone_has_its_top_above_and_nothing_below()
    {
        Assert.That(Edges(TwoTouches, price: 99.8m), Is.EqualTo(((decimal?)100.3m, (decimal?)null)));
    }

    [Test]
    public void A_lone_touch_is_no_zone()
    {
        decimal[] highs = [99, 99.5m, 105, 99.5m, 99, 99, 99, 99, 99, 99];

        Assert.That(Edges(highs, price: 100), Is.EqualTo(((decimal?)null, (decimal?)null)));
        Assert.That(Edges(highs, price: 100, strength: 1), Is.EqualTo(((decimal?)105m, (decimal?)null)));
    }

    [Test]
    public void Touches_further_apart_than_the_merge_are_two_zones()
    {
        Assert.That(Edges(TwoTouches, price: 99.8m, atr: 0.5m), Is.EqualTo(((decimal?)null, (decimal?)null)));
        Assert.That(Edges(TwoTouches, price: 99.8m, atr: 0.5m, strength: 1), Is.EqualTo(((decimal?)100.0m, (decimal?)null)));
    }

    [Test]
    public void A_flat_top_is_one_touch_on_its_first_candle()
    {
        decimal[] highs = [99, 99.5m, 101, 101, 99.5m, 99, 99, 99, 99, 99];

        Assert.That(Edges(highs, price: 100), Is.EqualTo(((decimal?)null, (decimal?)null)), "two touches would make a zone");
        Assert.That(Edges(highs, price: 100, strength: 1), Is.EqualTo(((decimal?)101m, (decimal?)null)));
    }

    [Test]
    public void The_nearest_of_two_zones_above_gives_the_edge()
    {
        decimal[] highs = [.. TwoTouches, 99.5m, 103.8m, 99.5m, 99, 99.5m, 104.0m, 99.5m, 99];

        Assert.That(Edges(highs, price: 99.8m), Is.EqualTo(((decimal?)100.3m, (decimal?)null)));
        Assert.That(Edges(highs, price: 101), Is.EqualTo(((decimal?)104.0m, (decimal?)100.0m)));
    }

    [Test]
    public void The_current_candle_is_not_in_the_window()
    {
        ClosedCandle[] history = [.. Flat(TwoTouches)];
        ClosedCandle breaking = NodeGraphs.Bar(history.Length, 99.8m, 101.5m, 98.1m, 99.8m);
        ClosedCandle quiet = NodeGraphs.Bar(history.Length, 99.8m, 99.9m, 99.7m, 99.8m);

        Assert.That(Run([.. history, breaking], "above").Last(), Is.EqualTo(100.3m));
        Assert.That(Run([.. history, quiet], "above").Last(), Is.EqualTo(100.3m));
    }

    [Test]
    public void Fewer_candles_than_the_length_before_the_current_give_no_edges()
    {
        ClosedCandle[] candles = [.. Flat(TwoTouches), NodeGraphs.Bar(TwoTouches.Length, 99.8m, 99.8m, 99.8m, 99.8m)];

        decimal?[] above = Run(candles, "above", length: 11);

        Assert.That(above, Has.All.Null);
    }

    [TestCase(5, 0.5, 2)]
    [TestCase(20, 0.5, 2)]
    [TestCase(200, 0.5, 2)]
    [TestCase(200, 0.2, 3)]
    [TestCase(57, 1.5, 1)]
    public void On_random_candles_the_edges_are_those_of_the_zones_found_by_a_plain_count(int length, double merge, int strength)
    {
        var random = new Random(length * 31 + strength);
        ClosedCandle[] candles = [.. RandomCandles(random, 1500)];
        var share = (decimal)merge;

        decimal?[] above = Run(candles, "above", length, share, strength, atrFromRange: true);
        decimal?[] below = Run(candles, "below", length, share, strength, atrFromRange: true);

        for (int index = 0; index < candles.Length; index++)
        {
            (decimal? Above, decimal? Below) expected = Naive(candles, index, length, share, strength);
            Assert.That((above[index], below[index]), Is.EqualTo(expected), $"candle {index}");
        }
    }

    /// <summary>
    ///     The edges at the candle after the highs given, with flat lows; <c>price</c> is its close.
    /// </summary>
    private static (decimal? Above, decimal? Below) Edges(decimal[] highs, decimal price, decimal atr = 1, int strength = 2)
    {
        ClosedCandle[] candles = [.. Flat(highs), NodeGraphs.Bar(highs.Length, price, price, price, price)];
        return (Run(candles, "above", highs.Length, atr: atr, strength: strength).Last(), Run(candles, "below", highs.Length, atr: atr, strength: strength).Last());
    }

    private static IEnumerable<ClosedCandle> Flat(decimal[] highs)
    {
        return highs.Select((high, index) => NodeGraphs.Bar(index, high - 0.1m, high, 90, high - 0.1m));
    }

    // The node with the close as price; atr is written, or the range of the current candle when atrFromRange.
    private static decimal?[] Run(
        ClosedCandle[] candles,
        string output,
        int length = 10,
        decimal merge = 0.5m,
        int strength = 2,
        decimal atr = 1,
        bool atrFromRange = false)
    {
        string written = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"\"length\": {length}, \"merge\": {merge}, \"strength\": {strength}");
        string json = NodeGraphs.Graph(
            "number",
            $"z.{output}",
            $$"""
              { "id": "c", "kind": "candle" }, { "id": "range", "kind": "subtract" },
              { "id": "z", "kind": "zones", "inputs": { {{written}}{{(atrFromRange ? "" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $", \"atr\": {atr}"))}} } }
              """,
            $$"""
              { "from": "c.high", "to": "range.a" }, { "from": "c.low", "to": "range.b" }, { "from": "c.close", "to": "z.price" }
              {{(atrFromRange ? """, { "from": "range.result", "to": "z.atr" }""" : "")}}
              """);
        return NodeGraphs.Numbers(json, candles);
    }

    private static IEnumerable<ClosedCandle> RandomCandles(Random random, int count)
    {
        decimal price = 100;
        for (int index = 0; index < count; index++)
        {
            decimal open = price;
            price = Math.Max(1, price + Math.Round((decimal)((random.NextDouble() - 0.5) * 2), 1));
            decimal high = Math.Max(open, price) + Math.Round((decimal)random.NextDouble(), 1);
            decimal low = Math.Min(open, price) - Math.Round((decimal)random.NextDouble(), 1);
            yield return NodeGraphs.Bar(index, open, high, low, price);
        }
    }

    // ZoneFinder.Find of v2 on the window before the candle, as the spec tells it.
    private static (decimal? Above, decimal? Below) Naive(ClosedCandle[] candles, int index, int length, decimal merge, int strength)
    {
        if (index < length)
        {
            return (null, null);
        }

        ClosedCandle current = candles[index];
        decimal atr = current.High - current.Low;
        ClosedCandle[] window = candles[(index - length)..index];
        List<decimal> touches = [];
        for (int middle = 2; middle + 2 < window.Length; middle++)
        {
            decimal high = window[middle].High;
            decimal low = window[middle].Low;
            if (high > window[middle - 1].High && high > window[middle - 2].High && high >= window[middle + 1].High && high >= window[middle + 2].High)
            {
                touches.Add(high);
            }

            if (low < window[middle - 1].Low && low < window[middle - 2].Low && low <= window[middle + 1].Low && low <= window[middle + 2].Low)
            {
                touches.Add(low);
            }
        }

        touches.Sort();
        List<(decimal Bottom, decimal Top, int Count)> zones = [];
        foreach (decimal touch in touches)
        {
            if (zones.Count > 0 && touch - zones[^1].Bottom <= merge * atr)
            {
                zones[^1] = (zones[^1].Bottom, touch, zones[^1].Count + 1);
            }
            else
            {
                zones.Add((touch, touch, 1));
            }
        }

        List<(decimal Bottom, decimal Top, int Count)> strong = [.. zones.Where(zone => zone.Count >= strength)];
        decimal price = current.Close;
        decimal? above = strong.Where(zone => zone.Top >= price).Select(zone => (decimal?)zone.Top).Min();
        decimal? below = strong.Where(zone => zone.Bottom <= price).Select(zone => (decimal?)zone.Bottom).Max();
        return (above, below);
    }
}
