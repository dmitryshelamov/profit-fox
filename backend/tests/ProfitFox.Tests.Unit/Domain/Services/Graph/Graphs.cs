using System.Text.Json.Nodes;

namespace ProfitFox.Tests.Unit.Domain.Services.Graph;

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
    ///     The strategy of H0001: its signal with a short entry of 100 USDT on the event, the position and a close after 4h,
    ///     wired by orders and the position (change add-position-node, spec strategy-graph «Стратегия H0001»).
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
            { "id": "enter", "kind": "enter", "settings": { "side": "short" }, "inputs": { "size": 100 } },
            { "id": "position", "kind": "position" },
            { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } }
          ],
          "wires": [
            { "from": "rate.value", "to": "high.a" },
            { "from": "p.threshold", "to": "high.b" },
            { "from": "high.result", "to": "first.value" },
            { "from": "first.event", "to": "enter.when" },
            { "from": "enter.order", "to": "position.orders" },
            { "from": "position.position", "to": "close-after.position" },
            { "from": "close-after.order", "to": "position.orders" }
          ]
        }
        """;

    /// <summary>
    ///     The ladder of v2 in the model of change add-position-node (design.md, decision 14): 13 nodes, entered by a market
    ///     order while there is no position, averaged by a limit buy below the last entry while the deposit allows, and
    ///     closed by a limit sell of the whole quantity above the average price.
    /// </summary>
    public static string Ladder => File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "ladder.json"));

    /// <summary>
    ///     A strategy written in the form before change add-position-node, with the node <c>position</c> added: a size of
    ///     100 for each <c>enter</c> without one, its order into the position, and for each exit node the position into it
    ///     and its order back. A graph that has a <c>position</c> is returned as it is. This only spares the older tests
    ///     their wires; the reading of saved strategies has its own lift (task 6.1).
    /// </summary>
    public static string Raised(string json)
    {
        JsonObject graph = JsonNode.Parse(json)!.AsObject();
        if (graph["nodes"] is not JsonArray nodes || nodes.Any(node => (string?)node!["kind"] == "position"))
        {
            return json;
        }

        if (graph["wires"] is not JsonArray)
        {
            graph["wires"] = new JsonArray();
        }

        nodes.Add(new JsonObject { ["id"] = "position", ["kind"] = "position" });
        foreach (JsonObject node in nodes.Select(node => node!.AsObject()).ToList())
        {
            string id = (string)node["id"]!;
            switch ((string?)node["kind"])
            {
                case "enter":
                    if (node["inputs"] is not JsonObject inputs)
                    {
                        inputs = new JsonObject();
                        node["inputs"] = inputs;
                    }

                    inputs.TryAdd("size", 100);
                    graph.AddWire($"{id}.order", "position.orders");
                    break;
                case "stop-loss" or "take-profit" or "close-after":
                    graph.AddWire("position.position", $"{id}.position");
                    graph.AddWire($"{id}.order", "position.orders");
                    break;
            }
        }

        return graph.ToJsonString();
    }

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

    /// <summary>
    ///     An exit node with one written input, reading the position and giving its order to it.
    /// </summary>
    public static void AddExit(this JsonObject graph, string id, string kind, string input, JsonNode value)
    {
        graph.AddNode(id, kind, input, value);
        graph.AddWire("position.position", $"{id}.position");
        graph.AddWire($"{id}.order", "position.orders");
    }

    public static void RemoveNode(this JsonObject graph, string id)
    {
        graph.Nodes().Remove(graph.Node(id));
    }

    /// <summary>
    ///     The node and every wire from or to it.
    /// </summary>
    public static void RemoveNodeAndWires(this JsonObject graph, string id)
    {
        foreach (JsonNode wire in graph.Wires().Where(wire => ((string)wire!["from"]!).StartsWith(id + ".", StringComparison.Ordinal)
                                                              || ((string)wire["to"]!).StartsWith(id + ".", StringComparison.Ordinal)).ToList()!)
        {
            graph.Wires().Remove(wire);
        }

        graph.RemoveNode(id);
    }

    /// <summary>
    ///     An <c>enter</c> node with its side and a size of 100; its event still needs a wire. In a strategy its order goes
    ///     into the node <c>position</c>.
    /// </summary>
    public static void AddEnter(this JsonObject graph, string id, string side, bool intoPosition = true)
    {
        graph.Nodes().Add(new JsonObject
        {
            ["id"] = id,
            ["kind"] = "enter",
            ["settings"] = new JsonObject { ["side"] = side },
            ["inputs"] = new JsonObject { ["size"] = 100 }
        });
        if (intoPosition)
        {
            graph.AddWire($"{id}.order", "position.orders");
        }
    }

    /// <summary>
    ///     Every wire into <paramref name="to" />, which may have several.
    /// </summary>
    public static void RemoveWires(this JsonObject graph, string to)
    {
        foreach (JsonNode wire in graph.Wires().Where(wire => (string)wire!["to"]! == to).ToList()!)
        {
            graph.Wires().Remove(wire);
        }
    }

    /// <summary>
    ///     The wire from <paramref name="from" /> to <paramref name="to" />.
    /// </summary>
    public static void RemoveWire(this JsonObject graph, string from, string to)
    {
        JsonNode wire = graph.Wires().Single(wire => (string)wire!["from"]! == from && (string)wire["to"]! == to)!;
        graph.Wires().Remove(wire);
    }
}
