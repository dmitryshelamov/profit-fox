using System.Text.Json;

using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entities.Signals;

/// <summary>
///     A saved signal: <c>funding-high</c> with the signal of H0001 (the funding rate crosses a threshold for the first time) and
///     a position for each node, version 1, unless the test gives what it checks.
/// </summary>
public static class SignalDocumentGenerator
{
    private const string FundingHigh =
        """
        {
          "formatVersion": 1,
          "parameters": [{ "name": "threshold", "type": "number", "value": 0.0005 }],
          "outputs": [{ "name": "entry", "type": "event" }],
          "nodes": [
            { "id": "p", "kind": "parameters" },
            { "id": "rate", "kind": "funding-rate" },
            { "id": "high", "kind": "compare", "settings": { "operation": "ge" } },
            { "id": "first", "kind": "first-time" },
            { "id": "out", "kind": "outputs" }
          ],
          "wires": [
            { "from": "rate.value", "to": "high.a" },
            { "from": "p.threshold", "to": "high.b" },
            { "from": "high.result", "to": "first.value" },
            { "from": "first.event", "to": "out.entry" }
          ]
        }
        """;

    public static SignalDocument Generate(
        string name = "funding-high",
        string? description = null,
        GraphDefinition? graph = null,
        IReadOnlyDictionary<string, NodePosition>? layout = null,
        int version = 1,
        Guid? id = null)
    {
        GraphDefinition definition = graph ?? GenerateGraph();
        return SignalDocument.Restore(
            id ?? Guid.NewGuid(),
            name,
            description,
            definition,
            layout ?? definition.Nodes.Select((node, index) => (node.Id, Position: new NodePosition(index * 100, Y: 0))).ToDictionary(StringComparer.Ordinal),
            version);
    }

    /// <summary>
    ///     The signal graph of H0001 as the engine reads it, with its threshold.
    /// </summary>
    public static GraphDefinition GenerateGraph(decimal threshold = 0.0005m)
    {
        using var document = JsonDocument.Parse(FundingHigh.Replace("0.0005", threshold.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        return StrategyGraph.ReadSignal(document.RootElement).Value!.Graph;
    }
}
