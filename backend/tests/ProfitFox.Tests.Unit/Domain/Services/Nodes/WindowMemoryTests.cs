using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Tests.Unit.Domain.Services.Execution;

namespace ProfitFox.Tests.Unit.Domain.Services.Nodes;

/// <summary>
///     Change add-setup-strategies, task 2.9: what the nodes of windows and indicators remember (spec strategy-execution
///     «Конечная память узлов окна», «Сходимость ATR и RSI», «Узлы окна при пересчёте после исполнения»).
/// </summary>
public sealed class WindowMemoryTests
{
    // window, percentile, hold and zones over 200, 200, 20 and 200 candles; hold keeps the close of the last rise.
    private const string Windows =
        """
        {
          "formatVersion": 1,
          "parameters": [],
          "outputs": [
            { "name": "high", "type": "number" }, { "name": "p10", "type": "number" },
            { "name": "rise", "type": "number" }, { "name": "above", "type": "number" }, { "name": "below", "type": "number" }
          ],
          "nodes": [
            { "id": "c", "kind": "candle" }, { "id": "prev", "kind": "previous" },
            { "id": "up", "kind": "compare", "settings": { "operation": "gt" } },
            { "id": "range", "kind": "subtract" },
            { "id": "w", "kind": "window", "settings": { "operation": "max" }, "inputs": { "length": 200 } },
            { "id": "p", "kind": "percentile", "inputs": { "length": 200, "rank": 0.1 } },
            { "id": "h", "kind": "hold", "inputs": { "length": 20 } },
            { "id": "z", "kind": "zones", "inputs": { "length": 200, "merge": 0.5, "strength": 2 } },
            { "id": "out", "kind": "outputs" }
          ],
          "wires": [
            { "from": "c.close", "to": "prev.value" }, { "from": "c.close", "to": "up.a" }, { "from": "prev.result", "to": "up.b" },
            { "from": "c.high", "to": "range.a" }, { "from": "c.low", "to": "range.b" },
            { "from": "c.high", "to": "w.value" }, { "from": "range.result", "to": "p.value" },
            { "from": "up.result", "to": "h.when" }, { "from": "c.close", "to": "h.value" },
            { "from": "range.result", "to": "z.atr" }, { "from": "c.close", "to": "z.price" },
            { "from": "w.result", "to": "out.high" }, { "from": "p.result", "to": "out.p10" }, { "from": "h.result", "to": "out.rise" },
            { "from": "z.above", "to": "out.above" }, { "from": "z.below", "to": "out.below" }
          ]
        }
        """;

    [Test]
    public void A_history_from_the_start_and_its_tail_of_500_give_the_same_windows()
    {
        ClosedCandle[] candles = [.. IndicatorNodeTests.RandomCandles(new Random(5), 3000)];

        List<PinValue[]> full = History.Run(Windows, candles);
        List<PinValue[]> tail = History.Run(Windows, candles[^500..]);

        // A window of 200 is full after 201 candles of the tail, the zones after 200 before the current one.
        Assert.That(tail.Skip(201).Select(Numbers), Is.EqualTo(full[^299..].Select(Numbers)));
        Assert.That(tail.Skip(201).Count(row => row.All(value => value.Number is not null)), Is.GreaterThan(100), "the windows give values");
    }

    [TestCase("atr", 14)]
    [TestCase("atr", 20)]
    [TestCase("rsi", 14)]
    [TestCase("rsi", 20)]
    public void Atr_and_rsi_up_to_twenty_converge_within_a_billionth_after_500_candles(string kind, int length)
    {
        (decimal full, decimal tail) = FullAndTail(kind, length);

        Assert.That(Math.Abs(full - tail) / full, Is.LessThan(1e-9m), $"{full} and {tail}");
    }

    [TestCase("atr")]
    [TestCase("rsi")]
    public void Atr_and_rsi_of_a_hundred_differ_by_more_than_a_billionth_after_500_candles(string kind)
    {
        (decimal full, decimal tail) = FullAndTail(kind, 100);

        Assert.That(Math.Abs(full - tail) / full, Is.GreaterThan(1e-9m), $"{full} and {tail}");
    }

    [TestCase("""{ "id": "n", "kind": "window", "settings": { "operation": "median" }, "inputs": { "length": 3 } }""", "value", "result")]
    [TestCase("""{ "id": "n", "kind": "percentile", "inputs": { "length": 3, "rank": 0.5 } }""", "value", "result")]
    [TestCase("""{ "id": "n", "kind": "hold", "inputs": { "length": 2, "when": true } }""", "value", "result")]
    [TestCase("""{ "id": "n", "kind": "atr", "inputs": { "length": 2 } }""", null, "result")]
    [TestCase("""{ "id": "n", "kind": "rsi", "inputs": { "length": 2 } }""", "value", "result")]
    [TestCase("""{ "id": "n", "kind": "zones", "inputs": { "length": 6, "merge": 5, "strength": 1, "atr": 1 } }""", "price", "below")]
    public void A_computation_again_after_a_fill_gives_what_the_close_gave_and_moves_nothing(string node, string? valuePin, string output)
    {
        string wires = valuePin is null ? "" : $$"""{ "from": "c.close", "to": "n.{{valuePin}}" }""";
        string json = NodeGraphs.Graph("number", $"n.{output}", $$"""{ "id": "c", "kind": "candle" }, {{node}}""", wires);
        ClosedCandle[] candles = [.. IndicatorNodeTests.RandomCandles(new Random(11), 40)];
        GraphRun plain = NodeGraphs.Run(json);
        GraphRun again = NodeGraphs.Run(json);
        List<decimal?> withFills = [];
        List<decimal?> withoutFills = [];

        foreach (ClosedCandle candle in candles)
        {
            decimal? atClose = again.Step(candle, facts: null)[0].Number;
            decimal? afterFill = again.Again(PositionState.Closed, candle.CloseTime.AddMinutes(1))[0].Number;
            Assert.That(afterFill, Is.EqualTo(atClose), candle.OpenTime.ToString("O"));
            withFills.Add(atClose);
            withoutFills.Add(plain.Step(candle, facts: null)[0].Number);
        }

        Assert.That(withFills, Is.EqualTo(withoutFills));
        Assert.That(withFills.Count(value => value is not null), Is.GreaterThan(5), "the node gives values to compare");
    }

    /// <summary>
    ///     A node whose length is written or comes from a parameter keeps no more than its window (design.md, decision 3): a
    ///     ring of the longest window for each node of each pair did not fit the scanner in 6 GB. A length from another node
    ///     may grow, so that node keeps the longest window from the start.
    /// </summary>
    [TestCase("window")]
    [TestCase("percentile")]
    [TestCase("atr")]
    [TestCase("rsi")]
    [TestCase("zones")]
    public void A_fixed_length_keeps_no_ring_of_the_longest_window(string kind)
    {
        ClosedCandle[] candles = [.. IndicatorNodeTests.RandomCandles(new Random(3), 30)];
        string written = NumberNodeTests.Lengthed(kind, "20");
        string parameter = NumberNodeTests.Lengthed(kind, length: null, """{ "name": "len", "type": "number", "value": 20 }""");
        string wired = Wired(kind);
        // A run of the graph keeps about 11 KB besides the node; the ring of the longest window alone was 8 to 32 KB.
        Assert.That(Kept(written, candles), Is.LessThan(16 * 1024), "a written length keeps the window of 20");
        Assert.That(Kept(parameter, candles), Is.LessThan(16 * 1024), "a length from a parameter keeps the window of 20");
        Assert.That(NodeGraphs.Numbers(wired, candles), Is.EqualTo(NodeGraphs.Numbers(written, candles)), "a length from a node gives the same numbers");
    }

    // What a run of the graph stepped over the candles keeps, on average over many runs.
    private static long Kept(string json, ClosedCandle[] candles)
    {
        const int Runs = 200;
        var runs = new GraphRun[Runs];
        GC.Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);
        for (int index = 0; index < Runs; index++)
        {
            runs[index] = NodeGraphs.Run(json);
            foreach (ClosedCandle candle in candles)
            {
                runs[index].Step(candle, facts: null);
            }
        }

        long after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(runs);
        return (after - before) / Runs;
    }

    // The graph of Lengthed with its length of 20 coming from a node: the larger of 20 and 1, which may change.
    private static string Wired(string kind)
    {
        string json = NumberNodeTests.Lengthed(kind, length: null, """{ "name": "len", "type": "number", "value": 20 }""");
        return json
            .Replace("""{ "from": "p.len", "to": "n.length" }""", """{ "from": "p.len", "to": "m.a" }, { "from": "m.result", "to": "n.length" }""", StringComparison.Ordinal)
            .Replace("""{ "id": "c", "kind": "candle" }""", """{ "id": "c", "kind": "candle" }, { "id": "m", "kind": "max", "inputs": { "b": 1 } }""", StringComparison.Ordinal);
    }

    // The value at the last candle from a history of 3000 candles and from its last 500.
    private static (decimal Full, decimal Tail) FullAndTail(string kind, int length)
    {
        ClosedCandle[] candles = [.. IndicatorNodeTests.RandomCandles(new Random(length + kind.Length), 3000)];
        string json = kind == "atr"
            ? NodeGraphs.Graph("number", "n.result", $$"""{ "id": "n", "kind": "atr", "inputs": { "length": {{length}} } }""")
            : NodeGraphs.OfClose("rsi", "value", $"\"length\": {length}");

        return (NodeGraphs.Numbers(json, candles)[^1]!.Value, NodeGraphs.Numbers(json, candles[^500..])[^1]!.Value);
    }

    private static decimal?[] Numbers(PinValue[] row)
    {
        return [.. row.Select(value => value.Number)];
    }
}
