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
        return [.. candles.Select(candle => executor.Value("BTCUSDT", candle, lastFunding: null))];
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
        return [.. candles.Select(candle => run.Step(candle, lastFunding: null)[0].Number)];
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
