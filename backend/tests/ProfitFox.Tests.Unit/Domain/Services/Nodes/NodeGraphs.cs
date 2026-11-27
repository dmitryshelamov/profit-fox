using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Tests.Unit.Domain.Services.Execution;

namespace ProfitFox.Tests.Unit.Domain.Services.Nodes;

/// <summary>
///     Graphs and candles for the tests of the nodes of change add-setup-strategies: a graph with any parameters and one
///     output, candles with their own open, high, low, close and volume, and a run that keeps the graph between candles.
/// </summary>
internal static class NodeGraphs
{
    /// <summary>
    ///     A graph with the parameters <paramref name="parameters" /> (JSON objects, may be empty), the nodes and wires given
    ///     and one output <c>x</c> of <paramref name="type" /> taken from <paramref name="from" />.
    /// </summary>
    public static string Graph(string type, string from, string nodes, string wires = "", string parameters = "")
    {
        return $$"""
                 {
                   "formatVersion": 1,
                   "parameters": [{{parameters}}],
                   "outputs": [{ "name": "x", "type": "{{type}}" }],
                   "nodes": [{{nodes}}, { "id": "out", "kind": "outputs" }],
                   "wires": [{{wires}}{{(wires.Length > 0 ? "," : "")}} { "from": "{{from}}", "to": "out.x" }]
                 }
                 """;
    }

    /// <summary>
    ///     The <paramref name="index" />-th 4h candle from <see cref="History.Start" />.
    /// </summary>
    public static ClosedCandle Bar(int index, decimal open, decimal high, decimal low, decimal close, decimal volume = 1)
    {
        DateTimeOffset start = History.Start + index * History.Bar;
        return new ClosedCandle(start, start + History.Bar, open, high, low, close, volume);
    }

    /// <summary>
    ///     Candles whose every price is the close, with the closes given.
    /// </summary>
    public static ClosedCandle[] Closes(params decimal[] closes)
    {
        return [.. History.Closes(closes)];
    }

    /// <summary>
    ///     The numbers of the only output at each candle.
    /// </summary>
    public static decimal?[] Numbers(string json, IEnumerable<ClosedCandle> candles)
    {
        return [.. History.Run(json, candles).Select(output => output[0].Number)];
    }

    /// <summary>
    ///     A graph whose output is <paramref name="from" /> of a node of <paramref name="kind" /> fed with the close of the
    ///     candle on <paramref name="valuePin" />, with the written inputs and settings given.
    /// </summary>
    public static string OfClose(string kind, string valuePin, string inputs, string settings = "", string from = "n.result")
    {
        string written = settings.Length > 0 ? $$""", "settings": { {{settings}} }""" : "";
        return Graph(
            "number",
            from,
            $$"""{ "id": "c", "kind": "candle" }, { "id": "n", "kind": "{{kind}}"{{written}}, "inputs": { {{inputs}} } }""",
            $$"""{ "from": "c.close", "to": "n.{{valuePin}}" }""");
    }

    /// <summary>
    ///     A run of <paramref name="json" /> over candles given one by one.
    /// </summary>
    public static GraphRun Run(string json)
    {
        return new GraphRun(History.Graph(json));
    }
}
