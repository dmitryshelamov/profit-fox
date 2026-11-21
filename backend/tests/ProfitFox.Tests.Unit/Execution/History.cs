using ProfitFox.Domain.Execution;
using ProfitFox.Domain.Graph;
using ProfitFox.Domain.Graph.Model;
using ProfitFox.Domain.Graph.Reading;

namespace ProfitFox.Tests.Unit.Execution;

/// <summary>
///     Histories of 4h candles for the tests, and running a graph over them.
/// </summary>
internal static class History
{
    public static readonly DateTimeOffset Start = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
    public static readonly TimeSpan Bar = TimeSpan.FromHours(4);

    /// <summary>
    ///     The <paramref name="index" />-th 4h candle from <see cref="Start" /> with every price equal to <paramref name="close" />.
    /// </summary>
    public static ClosedCandle Candle(int index, decimal close, decimal volume = 1)
    {
        DateTimeOffset open = Start + index * Bar;
        return new ClosedCandle(open, open + Bar, close, close, close, close, volume);
    }

    public static IEnumerable<ClosedCandle> Closes(params decimal[] closes)
    {
        return closes.Select((close, index) => Candle(index, close));
    }

    public static GraphDefinition Graph(string json)
    {
        ReadResult<GraphDefinition> result = StrategyGraph.Read(json);
        Assert.That(result.Errors, Is.Empty, "the test graph must pass the checks");
        return result.Value!;
    }

    /// <summary>
    ///     The declared outputs of the graph at each candle; <paramref name="funding" /> gives the last payment by a close.
    /// </summary>
    public static List<PinValue[]> Run(string json, IEnumerable<ClosedCandle> candles, Func<ClosedCandle, FundingPayment?>? funding = null)
    {
        var run = new GraphRun(Graph(json));
        return [.. candles.Select(candle => run.Step(candle, funding?.Invoke(candle)).ToArray())];
    }

    /// <summary>
    ///     A graph with one number or boolean output <c>x</c> taken from <paramref name="from" />.
    /// </summary>
    public static string WithOutput(string type, string from, string nodes, string wires = "")
    {
        return $$"""
                 {
                   "formatVersion": 1,
                   "parameters": [{ "name": "threshold", "type": "number", "value": 0.0005 }],
                   "outputs": [{ "name": "x", "type": "{{type}}" }],
                   "nodes": [{{nodes}}, { "id": "out", "kind": "outputs" }],
                   "wires": [{{wires}}{{(wires.Length > 0 ? "," : "")}} { "from": "{{from}}", "to": "out.x" }]
                 }
                 """;
    }
}
