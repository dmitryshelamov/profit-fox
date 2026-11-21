using System.Text.Json.Nodes;

namespace ProfitFox.Tests.Unit.Graph;

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
    ///     The strategy of H0001: its signal with a short entry on the event and a close after 4h (change
    ///     add-strategy-graph, design.md, decision 2).
    /// </summary>
    public const string H0001Strategy =
        """
        {
          "formatVersion": 1,
          "parameters": [{ "name": "threshold", "type": "number", "value": 0.0005 }],
          "outputs": [],
          "nodes": [
            { "id": "p", "kind": "parameters" },
            { "id": "rate", "kind": "funding-rate" },
            { "id": "high", "kind": "compare", "settings": { "operation": "ge" } },
            { "id": "first", "kind": "first-time" },
            { "id": "enter", "kind": "enter", "settings": { "side": "short" } },
            { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } }
          ],
          "wires": [
            { "from": "rate.value", "to": "high.a" },
            { "from": "p.threshold", "to": "high.b" },
            { "from": "high.result", "to": "first.value" },
            { "from": "first.event", "to": "enter.when" }
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

    /// <summary>
    ///     The H0001 strategy changed by <paramref name="change" />.
    /// </summary>
    public static string ChangedStrategy(Action<JsonObject> change)
    {
        JsonObject graph = JsonNode.Parse(H0001Strategy)!.AsObject();
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

    /// <summary>
    ///     A node with one written input.
    /// </summary>
    public static void AddNode(this JsonObject graph, string id, string kind, string input, JsonNode value)
    {
        graph.Nodes().Add(new JsonObject { ["id"] = id, ["kind"] = kind, ["inputs"] = new JsonObject { [input] = value } });
    }

    public static void RemoveNode(this JsonObject graph, string id)
    {
        graph.Nodes().Remove(graph.Node(id));
    }

    /// <summary>
    ///     An <c>enter</c> node with its side; its event still needs a wire.
    /// </summary>
    public static void AddEnter(this JsonObject graph, string id, string side)
    {
        graph.Nodes().Add(new JsonObject { ["id"] = id, ["kind"] = "enter", ["settings"] = new JsonObject { ["side"] = side } });
    }
}