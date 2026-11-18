using System.Text.Json.Nodes;

namespace ProfitFox.Tests.Unit.Engine.Strategy.Graph;

/// <summary>
///     Graphs for the tests: the signal of H0001 and ways to change it.
/// </summary>
internal static class Graphs
{
    public const string H0001 =
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
    ///     The H0001 signal changed by <paramref name="change" />.
    /// </summary>
    public static string Changed(Action<JsonObject> change)
    {
        JsonObject graph = JsonNode.Parse(H0001)!.AsObject();
        change(graph);

        return graph.ToJsonString();
    }

    public static JsonArray Nodes(this JsonObject graph)
    {
        return graph["nodes"]!.AsArray();
    }

    public static JsonArray Wires(this JsonObject graph)
    {
        return graph["wires"]!.AsArray();
    }

    public static JsonObject Node(this JsonObject graph, string id)
    {
        return graph.Nodes().Single(node => (string)node!["id"]! == id)!.AsObject();
    }

    public static void RemoveWire(this JsonObject graph, string to)
    {
        JsonNode wire = graph.Wires().Single(wire => (string)wire!["to"]! == to)!;
        graph.Wires().Remove(wire);
    }

    public static void AddWire(this JsonObject graph, string from, string to)
    {
        graph.Wires().Add(new JsonObject { ["from"] = from, ["to"] = to });
    }

    public static void AddNode(this JsonObject graph, string id, string kind)
    {
        graph.Nodes().Add(new JsonObject { ["id"] = id, ["kind"] = kind });
    }
}