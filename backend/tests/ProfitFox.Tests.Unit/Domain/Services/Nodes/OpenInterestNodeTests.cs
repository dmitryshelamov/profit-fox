using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Unit.Domain.Services.Execution;

namespace ProfitFox.Tests.Unit.Domain.Services.Nodes;

/// <summary>
///     Change add-idea-score, task 4.2: spec strategy-execution «Открытый интерес» — the node «OI» gives the last point of
///     open interest before the close of the candle, nothing before the first one, and its growth over a candle by
///     «предыдущее».
/// </summary>
public sealed class OpenInterestNodeTests
{
    private static readonly DateTimeOffset Morning = new DateTimeOffset(2026, 11, 20, 4, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);

    private const string Value = """{ "id": "oi", "kind": "open-interest" }""";

    [Test]
    public void The_candle_of_4h_closed_at_noon_takes_the_point_of_11_55()
    {
        // Points every five minutes from 04:00 to 12:30, each worth its minutes since 04:00.
        OpenInterestPoint[] points = [.. Enumerable.Range(0, 103).Select(index => new OpenInterestPoint(Morning.AddMinutes(5 * index), 5 * index))];
        HistoryCandle candle = Candle(Morning.AddHours(4));

        List<PinValue[]> outputs = Run(History.WithOutput("number", "oi.value", Value), [candle], points);

        Assert.That(outputs.Single()[0].Number, Is.EqualTo((decimal)(7 * 60 + 55)), "the point of 11:55");
    }

    [Test]
    public void Before_the_first_point_the_value_and_a_comparison_with_it_are_missing()
    {
        const string compared = """
                                { "id": "oi", "kind": "open-interest" },
                                { "id": "big", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 0 } }
                                """;

        List<PinValue[]> value = Run(History.WithOutput("number", "oi.value", Value), [Candle(Morning)], []);
        List<PinValue[]> comparison = Run(History.WithOutput("boolean", "big.result", compared, """{ "from": "oi.value", "to": "big.a" }"""), [Candle(Morning)], []);

        Assert.That(value.Single()[0].IsMissing, Is.True);
        Assert.That(comparison.Single()[0].IsMissing, Is.True);
    }

    [Test]
    public void The_open_interest_divided_by_its_previous_value_gives_its_growth_over_the_candle()
    {
        const string growth = """
                              { "id": "oi", "kind": "open-interest" },
                              { "id": "prev", "kind": "previous" },
                              { "id": "ratio", "kind": "divide" }
                              """;
        const string wires = """
                             { "from": "oi.value", "to": "prev.value" }, { "from": "oi.value", "to": "ratio.a" }, { "from": "prev.result", "to": "ratio.b" }
                             """;
        DateTimeOffset eight = Morning.AddHours(4);
        OpenInterestPoint[] points = [new OpenInterestPoint(eight.AddMinutes(-5), 1_000_000m), new OpenInterestPoint(eight.AddHours(4).AddMinutes(-5), 1_060_000m)];

        List<PinValue[]> outputs = Run(History.WithOutput("number", "ratio.result", growth, wires), [Candle(Morning), Candle(Morning + FourHours)], points);

        Assert.That(outputs[1][0].Number, Is.EqualTo(1.06m));
    }

    private static HistoryCandle Candle(DateTimeOffset open)
    {
        return new HistoryCandle(open, "100", "101", "99", "100", "5", Turnover: null, BuyVolume: null);
    }

    private static List<PinValue[]> Run(string json, HistoryCandle[] candles, OpenInterestPoint[] points)
    {
        var run = new GraphRun(History.Graph(json));
        return [.. PairCandles.Closed(FourHours, candles, [], points).Select(closed => run.Step(closed.Candle, closed.Facts).ToArray())];
    }
}
