using System.Text.Json;

using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entities.Strategies;

/// <summary>
///     A saved strategy: <c>funding-short</c> — a signal node <c>s</c>, a short <c>enter</c> of 100 USDT on its event, a
///     <c>close-after</c> of 4h and the node <c>position</c> wired with them — with a position on the canvas for each
///     node, version 1, of the kind of its graph, unless the test gives what it checks.
/// </summary>
public static class StrategyDocumentGenerator
{
    public static StrategyDocument Generate(
        Guid signal,
        string name = "funding-short",
        string? description = null,
        GraphDefinition? graph = null,
        IReadOnlyDictionary<string, NodePosition>? layout = null,
        int version = 1,
        Guid? id = null,
        StrategyKind? kind = null)
    {
        GraphDefinition definition = graph ?? GenerateGraph(signal);
        return StrategyDocument.Restore(
            id ?? Guid.NewGuid(),
            name,
            description,
            definition,
            layout ?? definition.Nodes.Select((node, index) => (node.Id, Position: new NodePosition(index * 100, Y: 0))).ToDictionary(StringComparer.Ordinal),
            version,
            kind: kind);
    }

    /// <summary>
    ///     The graph of <c>funding-short</c> as written: its node <c>s</c> points at version <paramref name="signalVersion" />
    ///     of <paramref name="signal" />; without a signal it is the same strategy with the signal of H0001 written out.
    ///     <paramref name="legacy" /> gives the form saved before change add-position-node: no position, no size, no wires
    ///     of orders.
    /// </summary>
    public static GraphDefinition GenerateGraph(Guid? signal, int signalVersion = 1, string after = "4h", bool legacy = false)
    {
        string enter = legacy ? """{ "id": "enter", "kind": "enter", "settings": { "side": "short" } }"""
            : """{ "id": "enter", "kind": "enter", "settings": { "side": "short" }, "inputs": { "size": 100 } }""";
        string position = legacy ? "" : """, { "id": "position", "kind": "position" }""";
        string wires = legacy ? ""
            : """
              ,
                  { "from": "enter.order", "to": "position.orders" },
                  { "from": "position.position", "to": "close-after.position" },
                  { "from": "close-after.order", "to": "position.orders" }
              """;
        string json = signal is { } id
            ? $$"""
                {
                  "formatVersion": 1,
                  "nodes": [
                    { "id": "s", "kind": "signal", "settings": { "signal": "{{id}}", "version": "{{signalVersion}}" } },
                    {{enter}},
                    { "id": "close-after", "kind": "close-after", "inputs": { "after": "{{after}}" } }{{position}}
                  ],
                  "wires": [{ "from": "s.event", "to": "enter.when" }{{wires}}]
                }
                """
            : $$"""
                {
                  "formatVersion": 1,
                  "nodes": [
                    { "id": "rate", "kind": "funding-rate" },
                    { "id": "high", "kind": "compare", "settings": { "operation": "ge" }, "inputs": { "b": 0.0005 } },
                    { "id": "first", "kind": "first-time" },
                    {{enter}},
                    { "id": "close-after", "kind": "close-after", "inputs": { "after": "{{after}}" } }{{position}}
                  ],
                  "wires": [
                    { "from": "rate.value", "to": "high.a" },
                    { "from": "high.result", "to": "first.value" },
                    { "from": "first.event", "to": "enter.when" }{{wires}}
                  ]
                }
                """;
        using var document = JsonDocument.Parse(json);
        return StrategyGraph.ReadForm(document.RootElement).Value!;
    }
}
