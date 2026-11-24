using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Tests.Unit.Domain.Services.Graph;

/// <summary>
///     Saved signals for the tests of the <c>signal</c> node (change add-strategy-constructor): <c>funding-high</c> with two
///     versions of the H0001 graph — version 2 bounds its threshold to [0, 0.01] — and strategies that point at it.
/// </summary>
internal static class SignalLibrary
{
    public static readonly Guid FundingHigh = Guid.Parse("0b6f8d4e-3c2a-4f51-9d7e-2a1b3c4d5e6f");

    /// <summary>
    ///     The H0001 signal with its threshold bounded to [0, 0.01].
    /// </summary>
    public static readonly string BoundedH0001 = Graphs.Changed(graph =>
    {
        JsonObject threshold = graph["parameters"]![0]!.AsObject();
        threshold["minimum"] = 0;
        threshold["maximum"] = 0.01;
    });

    /// <summary>
    ///     Two signals-state (change add-ideas-and-market): <c>close-above</c> (<see cref="Graphs.CloseAbove" />) and
    ///     <c>funding-above</c> (<see cref="Graphs.FundingAbove" />), one version each.
    /// </summary>
    public static readonly Guid CloseAbove = Guid.Parse("7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f");

    public static readonly Guid FundingAbove = Guid.Parse("8d2e3f4a-5b6c-4d7e-9f80-a1b2c3d4e5f6");

    public static SignalSet Set { get; } = new SignalSet(
    [
        new SignalVersion(new SignalReference(FundingHigh, 1), "funding-high", LatestVersion: 2, Form(Graphs.H0001)),
        new SignalVersion(new SignalReference(FundingHigh, 2), "funding-high", LatestVersion: 2, Form(BoundedH0001)),
        new SignalVersion(new SignalReference(CloseAbove, 1), "close-above", LatestVersion: 1, Form(Graphs.CloseAbove)),
        new SignalVersion(new SignalReference(FundingAbove, 1), "funding-above", LatestVersion: 1, Form(Graphs.FundingAbove))
    ]);

    /// <summary>
    ///     A strategy on two signals-state: the outputs <c>value</c> of <c>b</c> (<c>close-above</c>) and <c>f</c>
    ///     (<c>funding-above</c>) joined by <c>and</c>, then <c>first-time</c> into a short <c>enter</c> of 100 USDT, the
    ///     position and a <c>close-after</c> of 4h (spec strategy-execution «Состояние как условие входа»).
    /// </summary>
    public static string StateStrategy()
    {
        return $$"""
                 {
                   "formatVersion": 1,
                   "nodes": [
                     { "id": "b", "kind": "signal", "settings": { "signal": "{{CloseAbove}}", "version": "1" } },
                     { "id": "f", "kind": "signal", "settings": { "signal": "{{FundingAbove}}", "version": "1" } },
                     { "id": "both", "kind": "and" },
                     { "id": "first", "kind": "first-time" },
                     { "id": "enter", "kind": "enter", "settings": { "side": "short" }, "inputs": { "size": 100 } },
                     { "id": "position", "kind": "position" },
                     { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } }
                   ],
                   "wires": [
                     { "from": "b.value", "to": "both.a" },
                     { "from": "f.value", "to": "both.b" },
                     { "from": "both.result", "to": "first.value" },
                     { "from": "first.event", "to": "enter.when" },
                     { "from": "enter.order", "to": "position.orders" },
                     { "from": "position.position", "to": "close-after.position" },
                     { "from": "close-after.order", "to": "position.orders" }
                   ]
                 }
                 """;
    }

    /// <summary>
    ///     The strategy of H0001 written with a signal node: <c>s</c> on <paramref name="version" /> of <c>funding-high</c>, a
    ///     short <c>enter</c> of 100 USDT on <c>s.event</c>, the position and a <c>close-after</c> of 4h.
    /// </summary>
    public static string Strategy(int version = 1)
    {
        return $$"""
                 {
                   "formatVersion": 1,
                   "nodes": [
                     { "id": "s", "kind": "signal", "settings": { "signal": "{{FundingHigh}}", "version": "{{version}}" } },
                     { "id": "enter", "kind": "enter", "settings": { "side": "short" }, "inputs": { "size": 100 } },
                     { "id": "position", "kind": "position" },
                     { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } }
                   ],
                   "wires": [
                     { "from": "s.event", "to": "enter.when" },
                     { "from": "enter.order", "to": "position.orders" },
                     { "from": "position.position", "to": "close-after.position" },
                     { "from": "close-after.order", "to": "position.orders" }
                   ]
                 }
                 """;
    }

    /// <summary>
    ///     The strategy of <see cref="Strategy" /> changed by <paramref name="change" />.
    /// </summary>
    public static string ChangedStrategy(Action<JsonObject> change, int version = 1)
    {
        JsonObject graph = JsonNode.Parse(Strategy(version))!.AsObject();
        change(graph);

        return graph.ToJsonString();
    }

    public static void SetInput(this JsonObject node, string input, JsonNode value)
    {
        if (node["inputs"] is not JsonObject inputs)
        {
            inputs = new JsonObject();
            node["inputs"] = inputs;
        }

        inputs[input] = value;
    }

    // The graph as written, without the checks of its meaning: what the store keeps for a version.
    private static GraphDefinition Form(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return StrategyGraph.ReadForm(document.RootElement).Value!;
    }
}
