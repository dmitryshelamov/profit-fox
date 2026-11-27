using ProfitFox.Domain.ValueObjects.Execution;

namespace ProfitFox.Tests.Unit.Domain.Services.Nodes;

/// <summary>
///     Change add-setup-strategies, tasks 2.4 and 2.5: <c>window</c>, <c>percentile</c> and <c>hold</c> (spec
///     strategy-execution «Окно значений», «Перцентиль», «Запомненное значение»), and on random histories the same numbers
///     as the window counted again from its values at every candle.
/// </summary>
public sealed class WindowNodeTests
{
    private static readonly string[] Operations = ["max", "min", "sum", "average", "median", "first"];

    [Test]
    public void Max_over_three_candles()
    {
        Assert.That(Window("max", 3, 3, 7, 5, 4), Is.EqualTo(new decimal?[] { null, null, 7, 7 }));
    }

    [Test]
    public void First_is_the_earliest_value_of_the_window()
    {
        Assert.That(Window("first", 3, 3, 7, 5, 4), Is.EqualTo(new decimal?[] { null, null, 3, 7 }));
    }

    [Test]
    public void The_median_of_an_even_window_is_the_mean_of_the_two_middle_values()
    {
        Assert.That(Window("median", 4, 1, 9, 3, 5)[3], Is.EqualTo(4m));
    }

    [TestCase("min", 3)]
    [TestCase("sum", 12)]
    [TestCase("average", 4)]
    [TestCase("median", 4)]
    public void The_other_operations_over_three_candles(string operation, decimal expected)
    {
        Assert.That(Window(operation, 3, 9, 3, 5, 4)[3], Is.EqualTo(expected));
    }

    [Test]
    public void A_window_not_yet_full_has_no_result()
    {
        Assert.That(Window("max", 20, [.. Enumerable.Range(1, 19).Select(index => (decimal)index)]), Has.All.Null);
        Assert.That(Window("max", 20, [.. Enumerable.Range(1, 20).Select(index => (decimal)index)])[19], Is.EqualTo(20m));
    }

    [Test]
    public void A_gap_in_the_window_gives_no_result_until_it_leaves()
    {
        // The value is close × volume / volume: a volume of 0 makes it missing.
        string json = WindowOfCandles("max", "3");
        ClosedCandle[] candles = [.. new[] { (5m, 1m), (6m, 1m), (7m, 0m), (4m, 1m), (3m, 1m), (2m, 1m) }.Select((pair, index) => NodeGraphs.Bar(index, pair.Item1, pair.Item1, pair.Item1, pair.Item1, pair.Item2))];

        Assert.That(NodeGraphs.Numbers(json, candles), Is.EqualTo(new decimal?[] { null, null, null, null, null, 4 }));
    }

    [TestCaseSource(nameof(Operations))]
    public void On_random_histories_a_window_is_the_window_counted_again(string operation)
    {
        var random = new Random(operation.GetHashCode(StringComparison.Ordinal) & 0xFFFF);
        foreach ((int shortLength, int longLength) in new[] { (1, 1), (3, 7), (20, 20), (200, 37), (1000, 999) })
        {
            List<(decimal Value, decimal Volume, bool Long)> rows = Rows(random, 2600);
            string json = WindowOfCandles(operation, LengthBySide(shortLength, longLength));

            decimal?[] actual = NodeGraphs.Numbers(json, Candles(rows));
            decimal?[] expected = [.. rows.Select((_, index) => Naive(operation, rows, index, rows[index].Long ? longLength : shortLength))];

            Assert.That(actual, Is.EqualTo(expected), $"{operation} {shortLength}/{longLength}");
        }
    }

    [Test]
    public void The_tenth_and_the_ninetieth_percentile_of_one_to_ten()
    {
        decimal[] values = [7, 3, 10, 1, 5, 9, 2, 8, 6, 4];

        Assert.That(Percentile(10, 0.1m, values)[9], Is.EqualTo(1m));
        Assert.That(Percentile(10, 0.9m, values)[9], Is.EqualTo(9m));
    }

    [TestCase("0")]
    [TestCase("1.5")]
    [TestCase("-0.1")]
    public void A_rank_outside_the_bounds_gives_no_result(string rank)
    {
        Assert.That(Percentile(3, decimal.Parse(rank, System.Globalization.CultureInfo.InvariantCulture), 1, 2, 3, 4), Has.All.Null);
    }

    [Test]
    public void A_rank_of_one_is_the_largest()
    {
        Assert.That(Percentile(3, 1, 1, 9, 3, 4)[3], Is.EqualTo(9m));
    }

    [Test]
    public void On_random_histories_a_percentile_is_the_rank_in_the_sorted_window()
    {
        var random = new Random(42);
        foreach ((int length, decimal rank) in new[] { (1, 0.5m), (10, 0.1m), (200, 0.1m), (200, 0.9m), (57, 0.33m), (1000, 1m) })
        {
            List<(decimal Value, decimal Volume, bool Long)> rows = Rows(random, 2600);
            string json = NodeGraphs.Graph(
                "number",
                "n.result",
                $$"""
                  { "id": "c", "kind": "candle" }, { "id": "one", "kind": "divide" }, { "id": "v", "kind": "multiply" },
                  { "id": "n", "kind": "percentile", "inputs": { "length": {{length}}, "rank": {{rank.ToString(System.Globalization.CultureInfo.InvariantCulture)}} } }
                  """,
                """
                { "from": "c.volume", "to": "one.a" }, { "from": "c.volume", "to": "one.b" },
                { "from": "c.close", "to": "v.a" }, { "from": "one.result", "to": "v.b" }, { "from": "v.result", "to": "n.value" }
                """);

            decimal?[] actual = NodeGraphs.Numbers(json, Candles(rows));
            decimal?[] expected = [.. rows.Select((_, index) => NaivePercentile(rows, index, length, rank))];

            Assert.That(actual, Is.EqualTo(expected), $"{length} {rank}");
        }
    }

    [Test]
    public void Hold_keeps_a_value_for_its_length_of_candles()
    {
        Assert.That(Hold(2, (true, 104), (false, 1), (false, 2), (false, 3)), Is.EqualTo(new decimal?[] { 104, 104, 104, null }));
    }

    [Test]
    public void A_new_true_condition_replaces_the_value_and_the_count_starts_from_it()
    {
        Assert.That(Hold(2, (true, 104), (false, 1), (true, 107), (false, 2), (false, 3), (false, 4)), Is.EqualTo(new decimal?[] { 104, 104, 107, 107, 107, null }));
    }

    [Test]
    public void A_true_condition_on_a_missing_value_holds_nothing_until_a_known_one()
    {
        // The value is close × volume / volume: a volume of 0 makes it missing.
        string json = NodeGraphs.Graph(
            "number",
            "n.result",
            """
            { "id": "c", "kind": "candle" }, { "id": "one", "kind": "divide" }, { "id": "v", "kind": "multiply" },
            { "id": "big", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 100 } },
            { "id": "n", "kind": "hold", "inputs": { "length": 5 } }
            """,
            """
            { "from": "c.volume", "to": "one.a" }, { "from": "c.volume", "to": "one.b" },
            { "from": "c.close", "to": "v.a" }, { "from": "one.result", "to": "v.b" }, { "from": "v.result", "to": "n.value" },
            { "from": "c.close", "to": "big.a" }, { "from": "big.result", "to": "n.when" }
            """);
        ClosedCandle[] candles = [.. new[] { (104m, 1m), (1m, 1m), (105m, 0m), (2m, 1m), (106m, 1m) }.Select((pair, index) => NodeGraphs.Bar(index, pair.Item1, pair.Item1, pair.Item1, pair.Item1, pair.Item2))];

        Assert.That(NodeGraphs.Numbers(json, candles), Is.EqualTo(new decimal?[] { 104, 104, null, null, 106 }));
    }

    [Test]
    public void A_missing_condition_counts_as_false()
    {
        // close > previous close is unknown at the first candle, then true on the second.
        string json = NodeGraphs.Graph(
            "number",
            "n.result",
            """
            { "id": "c", "kind": "candle" }, { "id": "p", "kind": "previous" },
            { "id": "up", "kind": "compare", "settings": { "operation": "gt" } },
            { "id": "n", "kind": "hold", "inputs": { "length": 1 } }
            """,
            """
            { "from": "c.close", "to": "p.value" }, { "from": "c.close", "to": "up.a" }, { "from": "p.result", "to": "up.b" },
            { "from": "up.result", "to": "n.when" }, { "from": "c.close", "to": "n.value" }
            """);

        Assert.That(NodeGraphs.Numbers(json, NodeGraphs.Closes(5, 6, 4, 3)), Is.EqualTo(new decimal?[] { null, 6, 6, null }));
    }

    private static decimal?[] Window(string operation, int length, params decimal[] values)
    {
        return NodeGraphs.Numbers(NodeGraphs.OfClose("window", "value", $"\"length\": {length}", $"\"operation\": \"{operation}\""), NodeGraphs.Closes(values));
    }

    private static decimal?[] Percentile(int length, decimal rank, params decimal[] values)
    {
        string inputs = $"\"length\": {length}, \"rank\": {rank.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return NodeGraphs.Numbers(NodeGraphs.OfClose("percentile", "value", inputs), NodeGraphs.Closes(values));
    }

    // The condition is close > 100; the value is the volume.
    private static decimal?[] Hold(int length, params (bool When, decimal Value)[] candles)
    {
        string json = NodeGraphs.Graph(
            "number",
            "n.result",
            $$"""
              { "id": "c", "kind": "candle" },
              { "id": "big", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 100 } },
              { "id": "n", "kind": "hold", "inputs": { "length": {{length}} } }
              """,
            """{ "from": "c.close", "to": "big.a" }, { "from": "big.result", "to": "n.when" }, { "from": "c.volume", "to": "n.value" }""");

        return NodeGraphs.Numbers(json, candles.Select((candle, index) => NodeGraphs.Bar(index, 1, 1, 1, candle.When ? 200 : 50, candle.Value)));
    }

    // A window of close × volume / volume — missing where the volume is 0 — with the length given as JSON.
    private static string WindowOfCandles(string operation, string length)
    {
        bool wired = length.StartsWith('{');
        string lengthInput = wired ? "" : $"\"length\": {length}";
        return NodeGraphs.Graph(
            "number",
            "n.result",
            $$"""
              { "id": "c", "kind": "candle" }, { "id": "one", "kind": "divide" }, { "id": "v", "kind": "multiply" },
              {{(wired ? length + "," : "")}}
              { "id": "n", "kind": "window", "settings": { "operation": "{{operation}}" }, "inputs": { {{lengthInput}} } }
              """,
            $$"""
              { "from": "c.volume", "to": "one.a" }, { "from": "c.volume", "to": "one.b" },
              { "from": "c.close", "to": "v.a" }, { "from": "one.result", "to": "v.b" }, { "from": "v.result", "to": "n.value" }
              {{(wired ? """, { "from": "side.result", "to": "len.when" }, { "from": "len.result", "to": "n.length" }, { "from": "c.high", "to": "side.a" }, { "from": "c.close", "to": "side.b" }""" : "")}}
              """);
    }

    // The length is the long one on a candle whose high is above its close, the short one on any other.
    private static string LengthBySide(int shortLength, int longLength)
    {
        return $$"""
                 { "id": "side", "kind": "compare", "settings": { "operation": "gt" } },
                 { "id": "len", "kind": "select", "inputs": { "a": {{longLength}}, "b": {{shortLength}} } }
                 """;
    }

    // Values of two decimals; a twentieth of them missing (volume 0); the long length on a tenth of the candles in runs.
    private static List<(decimal Value, decimal Volume, bool Long)> Rows(Random random, int count)
    {
        List<(decimal, decimal, bool)> rows = [];
        bool isLong = false;
        for (int index = 0; index < count; index++)
        {
            if (random.Next(100) < 2)
            {
                isLong = !isLong;
            }

            decimal value = Math.Round((decimal)(random.NextDouble() * 200) - 50, 2);
            if (random.Next(10) == 0)
            {
                value = Math.Round(value, 0);
            }

            rows.Add((value, random.Next(20) == 0 ? 0 : random.Next(1, 1000), isLong));
        }

        return rows;
    }

    private static IEnumerable<ClosedCandle> Candles(List<(decimal Value, decimal Volume, bool Long)> rows)
    {
        return rows.Select((row, index) => NodeGraphs.Bar(index, row.Value, row.Long ? row.Value + 1 : row.Value, row.Value, row.Value, row.Volume));
    }

    private static decimal? Naive(string operation, List<(decimal Value, decimal Volume, bool Long)> rows, int index, int length)
    {
        if (index + 1 < length)
        {
            return null;
        }

        List<(decimal Value, decimal Volume, bool Long)> window = rows.GetRange(index + 1 - length, length);
        if (window.Any(row => row.Volume == 0))
        {
            return null;
        }

        List<decimal> values = [.. window.Select(row => row.Value)];
        List<decimal> sorted = [.. values.Order()];
        return operation switch
        {
            "max" => values.Max(),
            "min" => values.Min(),
            "sum" => values.Sum(),
            "average" => values.Sum() / length,
            "median" => length % 2 == 1 ? sorted[length / 2] : (sorted[(length / 2) - 1] + sorted[length / 2]) / 2,
            _ => values[0]
        };
    }

    private static decimal? NaivePercentile(List<(decimal Value, decimal Volume, bool Long)> rows, int index, int length, decimal rank)
    {
        if (index + 1 < length)
        {
            return null;
        }

        List<(decimal Value, decimal Volume, bool Long)> window = rows.GetRange(index + 1 - length, length);
        if (window.Any(row => row.Volume == 0))
        {
            return null;
        }

        List<decimal> sorted = [.. window.Select(row => row.Value).Order()];
        return sorted[(int)Math.Ceiling(length * rank) - 1];
    }
}
