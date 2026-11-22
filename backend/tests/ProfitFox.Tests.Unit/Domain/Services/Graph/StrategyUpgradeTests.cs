using System.Text.Json;

using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Strategies;

namespace ProfitFox.Tests.Unit.Domain.Services.Graph;

/// <summary>
///     Change add-position-node, task 6.1 (design.md, decision 11; spec strategy-constructor «Подъём графа прежнего вида»):
///     a saved strategy of the form before the change is read with the node <c>position</c>, the size 100 of each entry and
///     the wires of orders and of the position, the same way at every reading.
/// </summary>
public sealed class StrategyUpgradeTests
{
    [Test]
    public void Funding_short_of_the_former_form_is_raised()
    {
        GraphDefinition former = StrategyDocumentGenerator.GenerateGraph(SignalLibrary.FundingHigh, legacy: true);

        GraphDefinition raised = StrategyUpgrade.Raise(former);

        Assert.That(raised.Nodes.Select(node => node.Id), Is.EqualTo(new[] { "s", "enter", "close-after", "position" }));
        Assert.That(raised.Wires.Select(wire => $"{wire.From} -> {wire.To}"), Is.EqualTo(new[]
        {
            "s.event -> enter.when", "enter.order -> position.orders", "position.position -> close-after.position", "close-after.order -> position.orders"
        }));
        Assert.That(raised.Nodes.Single(node => node.Id == "enter").Inputs["size"], Is.EqualTo(GraphValue.FromNumber(100)));
        Assert.That(StrategyGraph.Write(raised), Is.EqualTo(StrategyGraph.Write(StrategyDocumentGenerator.GenerateGraph(SignalLibrary.FundingHigh))), "the graph a new funding-short has");
        Assert.That(StrategyGraph.ReadStrategy(StrategyGraph.Write(raised), SignalLibrary.Set).Errors, Is.Empty);
    }

    [Test]
    public void The_stop_and_the_target_get_their_wires_in_the_order_of_the_nodes()
    {
        GraphDefinition former = Form(
            """
            {
              "formatVersion": 1,
              "nodes": [
                { "id": "stop", "kind": "stop-loss", "inputs": { "distance": 0.02 } },
                { "id": "enter", "kind": "enter", "settings": { "side": "long" }, "inputs": { "size": 50 } },
                { "id": "c", "kind": "candle" },
                { "id": "first", "kind": "first-time" },
                { "id": "above", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 1 } },
                { "id": "target", "kind": "take-profit", "inputs": { "distance": 0.04 } },
                { "id": "close-after", "kind": "close-after", "inputs": { "after": "1d" } }
              ],
              "wires": [{ "from": "c.close", "to": "above.a" }, { "from": "above.result", "to": "first.value" }, { "from": "first.event", "to": "enter.when" }]
            }
            """);

        GraphDefinition raised = StrategyUpgrade.Raise(former);

        Assert.That(raised.Wires.Skip(3).Select(wire => $"{wire.From} -> {wire.To}"), Is.EqualTo(new[]
        {
            "position.position -> stop.position", "stop.order -> position.orders",
            "enter.order -> position.orders",
            "position.position -> target.position", "target.order -> position.orders",
            "position.position -> close-after.position", "close-after.order -> position.orders"
        }));
        Assert.That(raised.Nodes.Single(node => node.Id == "enter").Inputs["size"], Is.EqualTo(GraphValue.FromNumber(50)), "a written size stays");
        Assert.That(StrategyGraph.ReadStrategy(StrategyGraph.Write(raised)).Errors, Is.Empty);
    }

    [Test]
    public void A_taken_id_gives_position_2()
    {
        string json = StrategyGraph.Write(StrategyDocumentGenerator.GenerateGraph(signal: null, legacy: true))
            .Replace("\"rate\"", "\"position\"", StringComparison.Ordinal)
            .Replace("rate.", "position.", StringComparison.Ordinal);

        GraphDefinition raised = StrategyUpgrade.Raise(Form(json));

        Assert.That(raised.Nodes.Select(node => (node.Id, node.Kind)), Does.Contain(("position", "funding-rate")).And.Contain(("position-2", "position")));
        Assert.That(raised.Wires.Select(wire => wire.To.ToString()), Does.Contain("position-2.orders"));
        Assert.That(StrategyGraph.ReadStrategy(StrategyGraph.Write(raised)).Errors, Is.Empty);
    }

    [Test]
    public void A_second_raise_gives_the_same_JSON_and_a_graph_with_the_position_is_not_changed()
    {
        GraphDefinition once = StrategyUpgrade.Raise(StrategyDocumentGenerator.GenerateGraph(SignalLibrary.FundingHigh, legacy: true));
        GraphDefinition again = StrategyUpgrade.Raise(StrategyDocumentGenerator.GenerateGraph(SignalLibrary.FundingHigh, legacy: true));

        Assert.That(StrategyGraph.Write(again), Is.EqualTo(StrategyGraph.Write(once)));
        Assert.That(StrategyUpgrade.Raise(once), Is.SameAs(once));
        GraphDefinition ladder = Form(Graphs.Ladder);
        Assert.That(StrategyUpgrade.Raise(ladder), Is.SameAs(ladder));
    }

    private static GraphDefinition Form(string json)
    {
        using var document = JsonDocument.Parse(json);
        return StrategyGraph.ReadForm(document.RootElement).Value!;
    }
}
