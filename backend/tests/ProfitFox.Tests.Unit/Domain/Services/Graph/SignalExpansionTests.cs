using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Tests.Unit.Domain.Services.Graph;

/// <summary>
///     Spec strategy-graph «Раскрытие узла сигнала» (change add-strategy-constructor, task 2.3): before it runs, a strategy
///     with <c>signal</c> nodes becomes a graph without them, in the order of design.md, decision 3.
/// </summary>
public sealed class SignalExpansionTests
{
    [Test]
    public void The_expansion_of_the_strategy_of_H0001()
    {
        GraphDefinition expanded = Read(SignalLibrary.Strategy()).Graph;

        Assert.That(expanded.Nodes.Select(node => node.Id), Is.EqualTo(new[] { "s/rate", "s/high", "s/first", "enter", "position", "close-after" }));
        Assert.That(expanded.Wires.Select(wire => $"{wire.From} -> {wire.To}"), Is.EqualTo(new[]
        {
            "s/first.event -> enter.when", "enter.order -> position.orders", "position.position -> close-after.position", "close-after.order -> position.orders",
            "s/rate.value -> s/high.a", "s/high.result -> s/first.value"
        }), "first the wires of the strategy, then the inner wires of the signal in the order of its graph");
        Assert.That(expanded.Nodes.Single(node => node.Id == "s/high").Inputs["b"], Is.EqualTo(GraphValue.FromNumber(0.0005m)));
        Assert.That(expanded.Nodes.Single(node => node.Id == "s/high").Settings["operation"], Is.EqualTo("ge"));
        Assert.That(expanded.Parameters, Is.Empty);
        Assert.That(expanded.Outputs, Is.Empty);
    }

    [Test]
    public void A_written_value_takes_the_place_of_the_parameter()
    {
        string json = SignalLibrary.ChangedStrategy(graph => graph.Node("s").SetInput("threshold", 0.001));

        GraphDefinition expanded = Read(json).Graph;

        Assert.That(expanded.Nodes.Single(node => node.Id == "s/high").Inputs["b"], Is.EqualTo(GraphValue.FromNumber(0.001m)));
    }

    [Test]
    public void A_wire_into_an_input_of_the_signal_node()
    {
        string json = SignalLibrary.ChangedStrategy(graph =>
        {
            graph["parameters"] = JsonNode.Parse("""[{ "name": "limit", "type": "number", "value": 0.002 }]""");
            graph.AddNode("p", "parameters");
            graph.AddWire("p.limit", "s.threshold");
        });

        GraphDefinition expanded = Read(json).Graph;

        Assert.That(expanded.Wires.Select(wire => $"{wire.From} -> {wire.To}"), Is.EqualTo(new[]
        {
            "s/first.event -> enter.when", "enter.order -> position.orders", "position.position -> close-after.position", "close-after.order -> position.orders",
            "s/rate.value -> s/high.a", "p.limit -> s/high.b", "s/high.result -> s/first.value"
        }));
        Assert.That(expanded.Nodes.Single(node => node.Id == "s/high").Inputs, Does.Not.ContainKey("b"));
        Assert.That(expanded.Parameters.Single().Name, Is.EqualTo("limit"), "the parameters of the strategy stay");
    }

    [Test]
    public void A_taken_id()
    {
        string json = SignalLibrary.ChangedStrategy(graph => graph.AddNode("s/rate", "funding-rate"));

        GraphError error = StrategyGraph.ReadStrategy(json, SignalLibrary.Set).Errors.Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.ExpandedIdTaken));
        Assert.That(error.Node, Is.EqualTo("s"));
        Assert.That(error.Reason, Does.Contain("s/rate"));
    }

    [Test]
    public void The_ids_of_two_expansions_do_not_meet()
    {
        string json = SignalLibrary.ChangedStrategy(graph =>
        {
            // s expands into s/rate, s/high, s/first; s/x into s/x/rate, s/x/high, s/x/first.
            graph.Nodes().Add(JsonNode.Parse($$"""{ "id": "s/x", "kind": "signal", "settings": { "signal": "{{SignalLibrary.FundingHigh}}", "version": "1" } }"""));
        });

        Assert.That(StrategyGraph.ReadStrategy(json, SignalLibrary.Set).Errors, Is.Empty, "s/x/rate is not s/rate");
    }

    [Test]
    public void The_expanded_graph_passes_the_checks_of_a_strategy_and_the_source_is_not_changed()
    {
        string json = SignalLibrary.ChangedStrategy(graph => graph.Node("s").SetInput("threshold", 0.001));
        TradingStrategy strategy = Read(json);
        string source = StrategyGraph.Write(strategy.Source);

        Assert.That(StrategyGraph.ReadStrategy(StrategyGraph.Write(strategy.Graph)).Errors, Is.Empty);
        Assert.That(strategy.Source.Nodes.Select(node => node.Kind), Is.EqualTo(new[] { "signal", "enter", "position", "close-after" }));
        Assert.That(source, Is.EqualTo(StrategyGraph.Write(Read(json).Source)), "the same graph expands the same way");
        Assert.That(strategy.Entries, Is.EqualTo(new[] { new StrategyEntry("enter", EntrySide.Short) }));
    }

    [Test]
    public void A_strategy_without_signal_nodes_is_its_own_expansion()
    {
        TradingStrategy strategy = StrategyGraph.ReadStrategy(Graphs.H0001Strategy, SignalLibrary.Set).Value!;

        Assert.That(strategy.Graph, Is.SameAs(strategy.Source));
    }

    private static TradingStrategy Read(string json)
    {
        ReadResult<TradingStrategy> result = StrategyGraph.ReadStrategy(json, SignalLibrary.Set);
        Assert.That(result.Errors, Is.Empty);
        return result.Value!;
    }
}
