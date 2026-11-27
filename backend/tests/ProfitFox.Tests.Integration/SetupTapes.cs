using System.Globalization;
using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Tests.Integration;

/// <summary>
///     The histories of the scenarios of spec setup-strategies and the run of a signal of <c>Fixtures/Setups</c> over them.
/// </summary>
internal static class SetupTapes
{
    /// <summary>
    ///     30 steady candles at 100 with a range of 2: the ATR of the setup is 2 and the median of the volume 1000.
    /// </summary>
    public static SetupTape AtrOfTwo()
    {
        return new SetupTape().Steady(30, 100, 2);
    }

    /// <summary>
    ///     Candles that go from the last close by the steps given, each with a true range of 2, so the ATR stays 2.
    /// </summary>
    public static SetupTape Moves(this SetupTape tape, decimal volume, params decimal[] steps)
    {
        foreach (decimal step in steps)
        {
            decimal open = tape.Last.Close;
            decimal close = open + step;
            decimal wick = (2 - Math.Abs(step)) / 2;
            tape.Add(open, Math.Max(open, close) + wick, Math.Min(open, close) - wick, close, volume);
        }

        return tape;
    }

    /// <summary>
    ///     A history with the zone from 100.0 to 100.3 (spec strategy-execution «Зоны поддержки и сопротивления»): steady
    ///     candles at 99.1 with a range of 1 — the ATR is 1 — and two tops among the 200 candles before the next one, at 100.0
    ///     and at 100.3. The bottoms after each top make a zone at 98.6, far below. The volume is 1000.
    /// </summary>
    public static SetupTape ZoneOf100()
    {
        var tape = new SetupTape();
        tape.Steady(210, 99.1m, 1);
        Tops(tape, 99.8m, 100.0m, 99.8m);
        tape.Steady(3, 99.1m, 1);
        Tops(tape, 99.9m, 100.3m, 99.9m);
        return tape.Steady(4, 99.1m, 1);
    }

    /// <summary>
    ///     A history with the zone from 100.0 to 100.3 and no other (spec setup-strategies «Шорт без зоны снизу»): steady
    ///     candles at 99.1 with a range of 1 and two tops, at 100.0 and at 100.3, whose candles keep every true range at 1 —
    ///     the ATR is 1. The lows stay at 98.6 around the first top and go up only after one more candle at 98.6 before the
    ///     second, so they give a single touch at 98.6 and no zone below. The volume is 1000.
    /// </summary>
    public static SetupTape OnlyZoneOf100()
    {
        var tape = new SetupTape();
        tape.Steady(210, 99.1m, 1).Add(99.5m, 100.0m, 99.0m, 99.5m);
        tape.Steady(3, 99.1m, 1).Add(99.1m, 99.6m, 98.6m, 99.3m).Add(99.3m, 100.3m, 99.3m, 99.8m).Add(99.8m, 99.9m, 98.9m, 99.4m);
        return tape.Steady(4, 99.1m, 1);
    }

    /// <summary>
    ///     The edges <c>above</c> and <c>below</c> of the zones of the setups — 200 candles, a merge of 0.5 ATR of the
    ///     previous candle, two touches — about <paramref name="price" /> at the candle after the last of the tape.
    /// </summary>
    public static (decimal? Above, decimal? Below) EdgesAfter(SetupTape tape, decimal price)
    {
        List<ClosedCandle> candles = [.. tape.Candles, NextCandle(tape, price)];
        const string nodes = """
                             { "id": "atr", "kind": "atr", "inputs": { "length": 14 } }, { "id": "atr-prev", "kind": "previous" },
                             { "id": "zones", "kind": "zones", "inputs": { "length": 200, "merge": 0.5, "strength": 2 } }
                             """;
        const string wires = """
                             { "from": "atr.result", "to": "atr-prev.value" }, { "from": "atr-prev.result", "to": "zones.atr" },
                             { "from": "c.open", "to": "zones.price" }
                             """;
        return (Numbers(nodes, "zones.above", candles, wires)[^1], Numbers(nodes, "zones.below", candles, wires)[^1]);
    }

    private static ClosedCandle NextCandle(SetupTape tape, decimal open)
    {
        DateTimeOffset openTime = tape.Last.CloseTime;
        return new ClosedCandle(openTime, openTime + tape.Timeframe, open, open, open, open, 1000);
    }

    /// <summary>
    ///     Candles with the highs given, a range of 1 under each and the open and the close in the middle.
    /// </summary>
    public static void Tops(SetupTape tape, params decimal[] highs)
    {
        foreach (decimal high in highs)
        {
            tape.Add(high - 0.5m, high, high - 1, high - 0.5m);
        }
    }

    /// <summary>
    ///     The value of the signal of <c>Fixtures/Setups</c> at each candle, its parameters changed as given.
    /// </summary>
    public static List<bool?> States(string signal, IEnumerable<ClosedCandle> candles, params (string Name, decimal Value)[] parameters)
    {
        var executor = new SignalExecutor(Signal(signal, parameters));
        return [.. candles.Select(candle => executor.Value("BTCUSDT", candle, facts: null))];
    }

    public static Signal Signal(string name, params (string Name, decimal Value)[] parameters)
    {
        JsonObject graph = SetupFixtures.Body($"{name}.signal.json")["graph"]!.AsObject();
        foreach ((string parameter, decimal value) in parameters)
        {
            JsonObject written = graph["parameters"]!.AsArray().Select(item => item!.AsObject()).Single(item => (string?)item["name"] == parameter);
            written["value"] = JsonValue.Create(value);
        }

        ReadResult<Signal> read = StrategyGraph.ReadSignal(graph.ToJsonString());
        Assert.That(read.Errors, Is.Empty, $"the signal {name} passes the checks");
        return read.Value!;
    }

    /// <summary>
    ///     The value of a number of the graph at each candle: a graph of one node over the candle, such as the ATR of 14.
    /// </summary>
    public static List<decimal?> Numbers(string nodes, string from, IEnumerable<ClosedCandle> candles, string wires = "")
    {
        string json = $$"""
                        {
                          "formatVersion": 1,
                          "outputs": [{ "name": "x", "type": "number" }],
                          "nodes": [{ "id": "c", "kind": "candle" }, {{nodes}}, { "id": "out", "kind": "outputs" }],
                          "wires": [{{wires}}{{(wires.Length > 0 ? "," : "")}} { "from": "{{from}}", "to": "out.x" }]
                        }
                        """;
        var run = new GraphRun(StrategyGraph.Read(json).Value!);
        return [.. candles.Select(candle => run.Step(candle, facts: null)[0].Number)];
    }

    /// <summary>
    ///     The ATR of 14 of the candles at the last of them.
    /// </summary>
    public static decimal Atr(IEnumerable<ClosedCandle> candles)
    {
        return Numbers("""{ "id": "atr", "kind": "atr", "inputs": { "length": 14 } }""", "atr.result", candles)[^1]!.Value;
    }

    public static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
