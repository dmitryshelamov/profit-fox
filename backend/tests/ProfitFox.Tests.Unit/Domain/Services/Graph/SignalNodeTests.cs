using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Tests.Unit.Domain.Services.Graph;

/// <summary>
///     Spec strategy-graph «Узел сигнала», «Границы параметра на входе узла сигнала» and «Значение неподключённого входа»
///     (change add-strategy-constructor, tasks 2.1 and 2.2): the pins of a <c>signal</c> node come from the version it points
///     at, and the checks of a strategy know the saved signals.
/// </summary>
public sealed class SignalNodeTests
{
    [Test]
    public void Pins_by_the_parameters_of_the_version()
    {
        GraphDefinition graph = Form(SignalLibrary.Strategy());
        NodeDefinition node = graph.Nodes.Single(node => node.Id == "s");
        NodeKind kind = NodeCatalog.Find(NodeCatalog.Signal)!;

        Assert.That(kind.Inputs(node, graph, SignalLibrary.Set), Is.EqualTo(new[] { new PinDeclaration("threshold", PinType.Number) }));
        Assert.That(kind.Outputs(node, graph, SignalLibrary.Set), Is.EqualTo(new[] { new PinDeclaration("event", PinType.Event) }));
        Assert.That(Errors(SignalLibrary.Strategy()), Is.Empty);
    }

    [Test]
    public void No_such_version()
    {
        GraphError error = Errors(SignalLibrary.Strategy(version: 7)).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.UnknownSignal));
        Assert.That(error.Node, Is.EqualTo("s"));
        Assert.That(error.Setting, Is.EqualTo("version"));
        Assert.That(error.Reason, Does.Contain("no version 7"));
    }

    [Test]
    public void No_such_signal()
    {
        string json = SignalLibrary.ChangedStrategy(graph => graph.Node("s")["settings"]!["signal"] = Guid.NewGuid().ToString());

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.UnknownSignal));
        Assert.That(error.Setting, Is.EqualTo("signal"));
    }

    [Test]
    public void A_version_that_does_not_read_as_a_signal_is_reported_on_the_node()
    {
        GraphDefinition notASignal = Form(Graphs.H0001Strategy);
        var signals = new SignalSet([new SignalVersion(new SignalReference(SignalLibrary.FundingHigh, 1), "funding-high", 1, notASignal)]);

        GraphError error = StrategyGraph.ReadStrategy(SignalLibrary.Strategy(), signals).Errors.Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.UnreadableSignal));
        Assert.That(error.Node, Is.EqualTo("s"));
    }

    [TestCase("signal", "funding-high")]
    [TestCase("version", "02")]
    [TestCase("version", "0")]
    public void A_reference_of_the_wrong_form_is_an_invalid_setting(string setting, string value)
    {
        string json = SignalLibrary.ChangedStrategy(graph => graph.Node("s")["settings"]![setting] = value);

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.InvalidSetting));
        Assert.That((error.Node, error.Setting), Is.EqualTo(("s", setting)));
    }

    [Test]
    public void No_such_input_in_the_version()
    {
        string json = SignalLibrary.ChangedStrategy(graph =>
        {
            graph["parameters"] = JsonNode.Parse("""[{ "name": "limit", "type": "number", "value": 0.002 }]""");
            graph.AddNode("p", "parameters");
            graph.AddWire("p.limit", "s.limit");
        });

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.UnknownInput));
        Assert.That((error.Node, error.Pin), Is.EqualTo(("s", "limit")));
    }

    [Test]
    public void A_signal_node_in_a_signal()
    {
        string json = Graphs.Changed(graph =>
        {
            graph.Nodes().Add(JsonNode.Parse($$"""{ "id": "s", "kind": "signal", "settings": { "signal": "{{SignalLibrary.FundingHigh}}", "version": "1" } }"""));
        });

        GraphError error = StrategyGraph.ReadSignal(json).Errors.Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.SignalInSignal));
        Assert.That(error.Node, Is.EqualTo("s"));
    }

    [Test]
    public void An_input_of_a_signal_node_without_a_value()
    {
        GraphDefinition graph = Form(SignalLibrary.Strategy());

        Assert.That(graph.Nodes.Single(node => node.Id == "s").Inputs, Is.Empty);
        Assert.That(Errors(SignalLibrary.Strategy()), Is.Empty, "the input takes 0.0005, the value of the parameter");
    }

    [Test]
    public void A_value_below_the_minimum()
    {
        string json = SignalLibrary.ChangedStrategy(graph => graph.Node("s").SetInput("threshold", -0.1), version: 2);

        GraphError error = Errors(json).Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.ParameterOutOfBounds));
        Assert.That((error.Node, error.Pin), Is.EqualTo(("s", "threshold")));
    }

    [Test]
    public void A_value_within_the_bounds_and_a_wire_are_not_checked_against_them()
    {
        string written = SignalLibrary.ChangedStrategy(graph => graph.Node("s").SetInput("threshold", 0.001), version: 2);
        string wired = SignalLibrary.ChangedStrategy(graph =>
        {
            graph["parameters"] = JsonNode.Parse("""[{ "name": "limit", "type": "number", "value": -0.5 }]""");
            graph.AddNode("p", "parameters");
            graph.AddWire("p.limit", "s.threshold");
        }, version: 2);

        Assert.That(Errors(written), Is.Empty);
        Assert.That(Errors(wired), Is.Empty);
    }

    [Test]
    public void The_reading_without_saved_signals_refuses_a_signal_node_as_unknown()
    {
        GraphError error = StrategyGraph.ReadStrategy(SignalLibrary.Strategy()).Errors.Single();

        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.UnknownSignal));
        Assert.That(error.Node, Is.EqualTo("s"));
    }

    [Test]
    public void Every_error_of_a_strategy_with_a_signal_node_comes_at_once()
    {
        string json = SignalLibrary.ChangedStrategy(graph =>
        {
            graph.Node("enter")["settings"] = new JsonObject();
            graph.RemoveNodeAndWires("close-after");
        }, version: 7);

        List<GraphError> errors = Errors(json);

        Assert.That(errors.Select(error => error.Code), Is.EquivalentTo(new[]
        {
            GraphErrorCode.UnknownSignal, GraphErrorCode.MissingSetting, GraphErrorCode.StrategyExitMissing
        }));
    }

    [Test]
    public void The_references_of_a_graph_are_read_from_its_form()
    {
        string json = SignalLibrary.ChangedStrategy(graph =>
        {
            graph.Nodes().Add(JsonNode.Parse($$"""{ "id": "t", "kind": "signal", "settings": { "signal": "{{SignalLibrary.FundingHigh}}", "version": "2" } }"""));
            graph.Nodes().Add(JsonNode.Parse($$"""{ "id": "u", "kind": "signal", "settings": { "signal": "{{SignalLibrary.FundingHigh}}", "version": "1" } }"""));
        });

        Assert.That(SignalNodes.References(Form(json)), Is.EqualTo(new[]
        {
            new SignalReference(SignalLibrary.FundingHigh, 1), new SignalReference(SignalLibrary.FundingHigh, 2)
        }));
    }

    private static List<GraphError> Errors(string json)
    {
        return [.. StrategyGraph.ReadStrategy(json, SignalLibrary.Set).Errors];
    }

    private static GraphDefinition Form(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return StrategyGraph.ReadForm(document.RootElement).Value!;
    }
}
