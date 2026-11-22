using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Execution;

namespace ProfitFox.Tests.Unit.Domain.Services.Graph;

/// <summary>
///     Spec strategy-graph of change add-position-node (tasks 2.1, 2.3, 2.4 and 2.5): the types «заявка» and «позиция», the
///     input that takes several wires, the cycle through the node <c>position</c> and the rules of a strategy with it.
/// </summary>
public sealed class PositionGraphTests
{
    [TestCase(PinType.Order, "order")]
    [TestCase(PinType.Position, "position")]
    public void The_new_types_have_their_names(PinType type, string name)
    {
        Assert.That(PinTypes.Name(type), Is.EqualTo(name));
        Assert.That(PinTypes.TryParse(name, out PinType parsed) && parsed == type, Is.True);
    }

    [Test]
    public void An_order_is_not_a_number()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.AddNode("cmp", "compare", "b", 0);
            graph.Node("cmp")["settings"] = new JsonObject { ["operation"] = "gt" };
            graph.AddWire("enter.order", "cmp.a");
        });

        GraphError error = StrategyGraph.Read(json).Errors.Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.WireTypeMismatch));
        Assert.That(error.Wire, Is.EqualTo(new WireDefinition(new PinReference("enter", "order"), new PinReference("cmp", "a"))));
    }

    [Test]
    public void A_value_on_an_order_input_is_not_allowed()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.RemoveWires("position.orders");
            graph.Node("position")["inputs"] = new JsonObject { ["orders"] = 1 };
        });

        GraphError error = StrategyGraph.Read(json).Errors.Single();

        Assert.That((error.Code, error.Node, error.Pin), Is.EqualTo((GraphErrorCode.ValueNotAllowed, "position", "orders")));
    }

    [Test]
    public void A_value_on_a_position_input_is_not_allowed()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.RemoveWire("close-after.position");
            graph.Node("close-after").SetInput("position", 1);
        });

        GraphError error = StrategyGraph.Read(json).Errors.Single();

        Assert.That((error.Code, error.Node, error.Pin), Is.EqualTo((GraphErrorCode.ValueNotAllowed, "close-after", "position")));
    }

    [Test]
    public void Three_orders_go_into_the_position()
    {
        ReadResult<TradingStrategy> result = StrategyGraph.ReadStrategy(Graphs.Ladder);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Value!.Graph.Wires.Count(wire => wire.To == new PinReference("position", "orders")), Is.EqualTo(3));
    }

    [Test]
    public void An_ordinary_input_still_takes_one_wire()
    {
        string json = LadderChanged(graph => graph.AddWire("position.averagePrice", "buy.price"));

        GraphError error = StrategyGraph.ReadStrategy(json).Errors.Single();

        Assert.That((error.Code, error.Node, error.Pin), Is.EqualTo((GraphErrorCode.SecondWire, "buy", "price")));
    }

    [Test]
    public void An_unwired_position_input_needs_a_wire()
    {
        string json = Graphs.ChangedStrategy(graph => graph.RemoveWire("close-after.position"));

        GraphError error = StrategyGraph.ReadStrategy(json).Errors.Single();

        Assert.That((error.Code, error.Node, error.Pin), Is.EqualTo((GraphErrorCode.MissingInput, "close-after", "position")));
        Assert.That(error.Reason, Is.EqualTo("The input needs a wire."));
    }

    [Test]
    public void A_cycle_through_the_position_is_not_an_error()
    {
        Assert.That(StrategyGraph.Read(Graphs.H0001Strategy).Errors, Is.Empty);
        Assert.That(StrategyGraph.ReadStrategy(Graphs.Ladder).Errors, Is.Empty);
    }

    [Test]
    public void A_cycle_past_the_position_is_still_an_error()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.AddNode("x", "add", "b", 1);
            graph.AddNode("y", "multiply", "b", 2);
            graph.AddWire("x.result", "y.a");
            graph.AddWire("y.result", "x.a");
        });

        GraphError error = StrategyGraph.ReadStrategy(json).Errors.Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.Cycle));
        Assert.That(error.Nodes, Is.EqualTo(new[] { "x", "y" }));
    }

    /// <summary>
    ///     The position is written last, after the node that reads it; it is computed first all the same, so at the first
    ///     candle «не» of «открыта» is true and not missing.
    /// </summary>
    [Test]
    public void The_position_is_computed_before_the_nodes_that_read_it()
    {
        const string json =
            """
            {
              "formatVersion": 1,
              "outputs": [{ "name": "x", "type": "boolean" }],
              "nodes": [
                { "id": "closed", "kind": "not" },
                { "id": "entry", "kind": "market-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "size": 100 } },
                { "id": "out", "kind": "outputs" },
                { "id": "position", "kind": "position" }
              ],
              "wires": [
                { "from": "position.open", "to": "closed.value" },
                { "from": "closed.result", "to": "entry.when" },
                { "from": "entry.order", "to": "position.orders" },
                { "from": "closed.result", "to": "out.x" }
              ]
            }
            """;
        var run = new GraphRun(History.Graph(json));

        PinValue x = run.Step(History.Candle(0, 1), lastFunding: null)[0];

        Assert.That(x, Is.EqualTo(PinValue.FromBoolean(true)));
    }

    [Test]
    public void The_strategy_of_H0001_with_the_position_passes()
    {
        Assert.That(StrategyGraph.ReadStrategy(Graphs.H0001Strategy).Errors, Is.Empty);
    }

    [Test]
    public void A_ladder_without_close_after_passes()
    {
        ReadResult<TradingStrategy> result = StrategyGraph.ReadStrategy(Graphs.Ladder);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Value!.Graph.Nodes, Has.Count.EqualTo(13));
        Assert.That(result.Value.Graph.Nodes.Select(node => node.Kind), Has.None.AnyOf("enter", "close-after"));
    }

    [Test]
    public void A_strategy_without_an_opening_node_is_refused()
    {
        string json = LadderChanged(graph =>
        {
            foreach (string id in new[] { "entry", "buy", "sell" })
            {
                foreach (JsonNode wire in graph.Wires().Where(wire => ((string)wire!["from"]!).StartsWith(id + ".", StringComparison.Ordinal)
                                                                      || ((string)wire["to"]!).StartsWith(id + ".", StringComparison.Ordinal)).ToList()!)
                {
                    graph.Wires().Remove(wire);
                }

                graph.RemoveNode(id);
            }
        });

        var errors = StrategyGraph.ReadStrategy(json).Errors.ToList();

        Assert.That(errors.Select(error => error.Code), Does.Contain(GraphErrorCode.StrategyEntryMissing));
    }

    [Test]
    public void A_strategy_without_an_exit_is_refused()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.RemoveWire("close-after.order", "position.orders");
            graph.RemoveWire("close-after.position");
            graph.RemoveNode("close-after");
        });

        GraphError error = StrategyGraph.ReadStrategy(json).Errors.Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.StrategyExitMissing));
        Assert.That(error.Reason, Does.Contain("выход"));
    }

    [Test]
    public void A_strategy_without_the_position_is_refused()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.RemoveWires("position.orders");
            graph.RemoveWire("close-after.position");
            graph.RemoveNode("position");
            graph.Node("close-after").SetInput("after", "4h");
        });

        var errors = StrategyGraph.ReadStrategy(json).Errors.ToList();

        Assert.That(errors.Select(error => error.Code), Does.Contain(GraphErrorCode.StrategyPositionMissing));
        GraphError missing = errors.Single(error => error.Code == GraphErrorCode.StrategyPositionMissing);
        Assert.That(missing.Reason, Does.Contain("узел позиции"));
    }

    [Test]
    public void A_second_position_is_reported_on_its_node()
    {
        string json = Graphs.ChangedStrategy(graph =>
        {
            graph.AddNode("position-2", "position");
            graph.AddEnter("enter-2", "long", intoPosition: false);
            graph.AddWire("first.event", "enter-2.when");
            graph.AddWire("enter-2.order", "position-2.orders");
        });

        GraphError error = StrategyGraph.ReadStrategy(json).Errors.Single();

        Assert.That((error.Code, error.Node), Is.EqualTo((GraphErrorCode.StrategyPositionRepeated, "position-2")));
    }

    [Test]
    public void An_order_not_wired_into_the_position_is_reported_on_its_node()
    {
        string json = Graphs.ChangedStrategy(graph => graph.RemoveWire("enter.order", "position.orders"));

        GraphError error = StrategyGraph.ReadStrategy(json).Errors.Single();

        Assert.That((error.Code, error.Node), Is.EqualTo((GraphErrorCode.StrategyOrderUnwired, "enter")));
        Assert.That(error.Reason, Does.Contain("не подключена к позиции"));
    }

    [Test]
    public void A_position_node_in_a_signal_is_refused_by_the_check_of_a_signal()
    {
        string json = Graphs.Changed(graph => graph.AddNode("position", "position"));

        var errors = StrategyGraph.ReadSignal(json).Errors.ToList();

        Assert.That(errors.Select(error => (error.Code, error.Node)), Does.Contain((GraphErrorCode.SignalPositionNode, (string?)"position")));
    }

    [Test]
    public void Arithmetic_in_a_signal_passes()
    {
        string json = Graphs.Changed(graph =>
        {
            graph.RemoveWire("high.b");
            graph.AddNode("twice", "multiply", "b", 2);
            graph.AddWire("p.threshold", "twice.a");
            graph.AddWire("twice.result", "high.b");
        });

        Assert.That(StrategyGraph.ReadSignal(json).Errors, Is.Empty);
    }

    private static string LadderChanged(Action<JsonObject> change)
    {
        JsonObject graph = JsonNode.Parse(Graphs.Ladder)!.AsObject();
        change(graph);
        return graph.ToJsonString();
    }
}
