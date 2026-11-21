using System.Text.Json;

using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entities.Strategies;

/// <summary>
///     A saved strategy: <c>funding-short</c> — a signal node <c>s</c>, a short <c>enter</c> on its event and a
///     <c>close-after</c> of 4h — with a position for each node, version 1, unless the test gives what it checks.
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
        Guid? id = null)
    {
        GraphDefinition definition = graph ?? GenerateGraph(signal);
        return StrategyDocument.Restore(
            id ?? Guid.NewGuid(),
            name,
            description,
            definition,
            layout ?? definition.Nodes.Select((node, index) => (node.Id, Position: new NodePosition(index * 100, Y: 0))).ToDictionary(StringComparer.Ordinal),
            version);
    }

    /// <summary>
    ///     The graph of <c>funding-short</c> as written: its node <c>s</c> points at version <paramref name="signalVersion" />
    ///     of <paramref name="signal" />; without a signal it is the same strategy with the signal of H0001 written out.
    /// </summary>
    public static GraphDefinition GenerateGraph(Guid? signal, int signalVersion = 1, string after = "4h")
    {
        string json = signal is { } id
            ? $$"""
                {
                  "formatVersion": 1,
                  "nodes": [
                    { "id": "s", "kind": "signal", "settings": { "signal": "{{id}}", "version": "{{signalVersion}}" } },
                    { "id": "enter", "kind": "enter", "settings": { "side": "short" } },
                    { "id": "close-after", "kind": "close-after", "inputs": { "after": "{{after}}" } }
                  ],
                  "wires": [{ "from": "s.event", "to": "enter.when" }]
                }
                """
            : $$"""
                {
                  "formatVersion": 1,
                  "nodes": [
                    { "id": "rate", "kind": "funding-rate" },
                    { "id": "high", "kind": "compare", "settings": { "operation": "ge" }, "inputs": { "b": 0.0005 } },
                    { "id": "first", "kind": "first-time" },
                    { "id": "enter", "kind": "enter", "settings": { "side": "short" } },
                    { "id": "close-after", "kind": "close-after", "inputs": { "after": "{{after}}" } }
                  ],
                  "wires": [
                    { "from": "rate.value", "to": "high.a" },
                    { "from": "high.result", "to": "first.value" },
                    { "from": "first.event", "to": "enter.when" }
                  ]
                }
                """;
        using var document = JsonDocument.Parse(json);
        return StrategyGraph.ReadForm(document.RootElement).Value!;
    }
}
