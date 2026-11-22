using System.Text.Json.Nodes;

namespace ProfitFox.Tests.Integration;

/// <summary>
///     The signal of H0001, the first reference of the bench: the funding rate first at or above 0.0005.
/// </summary>
internal static class ReferenceSignals
{
    private const string Graph =
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

    /// <summary>
    ///     A new copy of the graph of the signal of H0001.
    /// </summary>
    public static JsonObject H0001()
    {
        return JsonNode.Parse(Graph)!.AsObject();
    }
}
