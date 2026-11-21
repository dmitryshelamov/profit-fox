using System.Text.Json.Nodes;

using ProfitFox.Domain.Graph;
using ProfitFox.Domain.Graph.Model;
using ProfitFox.Domain.Graph.Reading;
using ProfitFox.Domain.Nodes;

namespace ProfitFox.Tests.Unit.Graph;

/// <summary>
///     Spec strategy-graph: what a graph means — nodes, settings, inputs, wires, cycles, parameters and signals.
/// </summary>
public sealed class GraphValidationTests
{
    [Test]
    public void An_unknown_node_kind_is_reported_on_its_node()
    {
        string json = Graphs.Changed(graph => graph.AddNode("m", "macd"));

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.UnknownNodeKind));
        Assert.That(error.Node, Is.EqualTo("m"));
    }

    [Test]
    public void A_repeated_node_id_is_reported()
    {
        string json = Graphs.Changed(graph => graph.AddNode("rate", "funding-rate"));

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.DuplicateNodeId));
        Assert.That(error.Node, Is.EqualTo("rate"));
    }

    [Test]
    public void A_condition_wired_to_an_event_output_is_a_type_error()
    {
        string json = Graphs.Changed(graph =>
        {
            graph.RemoveWire("out.entry");
            graph.AddWire("high.result", "out.entry");
            graph.Nodes().Remove(graph.Node("first"));
            graph.RemoveWire("first.value");
        });

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.WireTypeMismatch));
        Assert.That(error.Wire, Is.EqualTo(new WireDefinition(new PinReference("high", "result"), new PinReference("out", "entry"))));
    }

    [Test]
    public void A_second_wire_into_one_input_is_reported_on_that_input()
    {
        const string json =
            """
            {
              "formatVersion": 1,
              "outputs": [{ "name": "both", "type": "boolean" }],
              "nodes": [
                { "id": "rate", "kind": "funding-rate" },
                { "id": "high", "kind": "compare", "settings": { "operation": "ge" }, "inputs": { "b": 0.0005 } },
                { "id": "first", "kind": "and" },
                { "id": "out", "kind": "outputs" }
              ],
              "wires": [
                { "from": "rate.value", "to": "high.a" },
                { "from": "high.result", "to": "first.a" },
                { "from": "high.result", "to": "first.a" },
                { "from": "high.result", "to": "first.b" },
                { "from": "first.result", "to": "out.both" }
              ]
            }
            """;

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.SecondWire));
        Assert.That((error.Node, error.Pin), Is.EqualTo(("first", "a")));
    }

    [Test]
    public void A_value_on_an_input_without_a_wire_is_taken()
    {
        string json = Graphs.Changed(graph =>
        {
            graph.RemoveWire("high.b");
            graph.Node("high")["inputs"] = new JsonObject { ["b"] = JsonNode.Parse("0.0005") };
        });

        GraphDefinition graph = StrategyGraph.Read(json).Value!;

        Assert.That(graph.Nodes.Single(node => node.Id == "high").Inputs["b"], Is.EqualTo(GraphValue.FromNumber(0.0005m)));
    }

    [Test]
    public void An_input_without_a_wire_and_without_a_value_is_reported()
    {
        string json = Graphs.Changed(graph => graph.RemoveWire("high.b"));

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.MissingInput));
        Assert.That((error.Node, error.Pin), Is.EqualTo(("high", "b")));
    }

    [Test]
    public void An_input_with_both_a_wire_and_a_value_is_reported()
    {
        string json = Graphs.Changed(graph => graph.Node("high")["inputs"] = new JsonObject { ["b"] = JsonNode.Parse("0.0005") });

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.WireAndValue));
        Assert.That((error.Node, error.Pin), Is.EqualTo(("high", "b")));
    }

    [Test]
    public void An_event_input_takes_no_value()
    {
        string json = Graphs.Changed(graph =>
        {
            graph.RemoveWire("out.entry");
            graph.Node("out")["inputs"] = new JsonObject { ["entry"] = true };
        });

        List<GraphError> errors = Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Node, error.Pin)), Does.Contain((GraphErrorCode.ValueNotAllowed, "out", "entry")));
    }

    [Test]
    public void A_value_of_the_wrong_type_is_reported()
    {
        string json = Graphs.Changed(graph =>
        {
            graph.RemoveWire("high.b");
            graph.Node("high")["inputs"] = new JsonObject { ["b"] = true };
        });

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.ValueTypeMismatch));
    }

    [Test]
    public void A_cycle_is_reported_with_its_nodes()
    {
        string json = Graphs.Changed(graph =>
        {
            graph.AddNode("a", "not");
            graph.AddNode("b", "not");
            graph.AddWire("a.result", "b.value");
            graph.AddWire("b.result", "a.value");
        });

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.Cycle));
        Assert.That(error.Nodes, Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void A_parameter_is_an_output_of_the_parameters_node()
    {
        GraphDefinition graph = StrategyGraph.Read(Graphs.H0001).Value!;

        IReadOnlyList<PinDeclaration> outputs = NodeCatalog.Find("parameters")!.Outputs(graph);

        Assert.That(outputs, Is.EqualTo(new[] { new PinDeclaration("threshold", PinType.Number) }));
        Assert.That(graph.Parameters[0].Value.Number, Is.EqualTo(0.0005m));
    }

    [Test]
    public void A_parameter_outside_its_bounds_is_reported()
    {
        string json = Graphs.Changed(graph =>
        {
            graph["parameters"]![0]!["minimum"] = 0;
            graph["parameters"]![0]!["value"] = JsonNode.Parse("-0.1");
        });

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.ParameterOutOfBounds));
        Assert.That(error.Parameter, Is.EqualTo("threshold"));
    }

    [Test]
    public void A_setting_is_checked_against_its_kind()
    {
        string json = Graphs.Changed(graph =>
        {
            graph.Node("high")["settings"] = new JsonObject { ["operation"] = "above", ["mode"] = "x" };
            graph.AddNode("low", "compare");
            graph.Node("low")["inputs"] = new JsonObject { ["a"] = 1, ["b"] = 2 };
        });

        List<GraphError> errors = Errors(json);

        Assert.That(
            errors.Select(error => (error.Code, error.Node, error.Setting)),
            Is.EquivalentTo(
                new[]
                {
                    (GraphErrorCode.UnknownSetting, "high", "mode"),
                    (GraphErrorCode.InvalidSetting, "high", "operation"),
                    (GraphErrorCode.MissingSetting, "low", "operation")
                }));
    }

    [Test]
    public void A_wire_to_a_missing_node_is_reported()
    {
        string json = Graphs.Changed(graph => graph.AddWire("nowhere.value", "high.a"));

        List<GraphError> errors = Errors(json);

        Assert.That(errors.Select(error => error.Code), Does.Contain(GraphErrorCode.UnknownNode));
    }

    [Test]
    public void A_signal_has_exactly_one_output()
    {
        string json = Graphs.Changed(graph =>
        {
            graph["outputs"]!.AsArray().Add(new JsonObject { ["name"] = "exit", ["type"] = "event" });
            graph.AddWire("first.event", "out.exit");
        });

        ReadResult<Signal> result = StrategyGraph.ReadSignal(json);

        Assert.That(result.Errors.Select(error => error.Code), Is.EqualTo(new[] { GraphErrorCode.SignalOutputCount }));
        Assert.That(StrategyGraph.Read(json).IsValid, Is.True, "two outputs are fine for a graph that is not a signal");
    }

    [Test]
    public void The_output_of_a_signal_is_an_event()
    {
        string json = Graphs.Changed(graph =>
        {
            graph["outputs"]![0]!["type"] = "boolean";
            graph.RemoveWire("out.entry");
            graph.AddWire("high.result", "out.entry");
        });

        ReadResult<Signal> result = StrategyGraph.ReadSignal(json);

        Assert.That(result.Errors.Select(error => error.Code), Does.Contain(GraphErrorCode.SignalOutputNotEvent));
    }

    [Test]
    public void Every_error_is_reported_at_once()
    {
        string json = Graphs.Changed(graph =>
        {
            graph.RemoveWire("first.value");
            graph.AddWire("rate.value", "first.value");
            graph.RemoveWire("high.b");
        });

        List<GraphError> errors = Errors(json);

        Assert.That(errors.Select(error => error.Code), Is.EquivalentTo(new[] { GraphErrorCode.WireTypeMismatch, GraphErrorCode.MissingInput }));
    }

    private static List<GraphError> Errors(string json)
    {
        ReadResult<GraphDefinition> result = StrategyGraph.Read(json);
        Assert.That(result.IsValid, Is.False, "the graph was expected to have errors");

        return [.. result.Errors];
    }
}