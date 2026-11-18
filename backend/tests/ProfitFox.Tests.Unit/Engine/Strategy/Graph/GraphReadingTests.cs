using System.Text.Json.Nodes;

using ProfitFox.Engine.Strategy.Graph;
using ProfitFox.Engine.Strategy.Graph.Model;
using ProfitFox.Engine.Strategy.Graph.Reading;

namespace ProfitFox.Tests.Unit.Engine.Strategy.Graph;

/// <summary>
///     Spec strategy-graph: the composition of a graph and the types of pins, as read from and written to JSON.
/// </summary>
public sealed class GraphReadingTests
{
    [Test]
    public void The_H0001_signal_is_read()
    {
        ReadResult<Signal> result = StrategyGraph.ReadSignal(Graphs.H0001);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Value!.Output, Is.EqualTo("entry"));
        Assert.That(result.Value.Graph.Nodes.Select(node => node.Kind), Is.EqualTo(new[] { "parameters", "funding-rate", "compare", "first-time", "outputs" }));
    }

    [Test]
    public void An_unknown_format_version_is_refused()
    {
        string json = Graphs.Changed(graph => graph["formatVersion"] = 2);

        ReadResult<GraphDefinition> result = StrategyGraph.Read(json);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Select(error => error.Code), Is.EqualTo(new[] { GraphErrorCode.UnsupportedFormatVersion }));
    }

    [Test]
    public void A_number_keeps_its_precision_when_written_and_read_back()
    {
        string json = Graphs.Changed(graph =>
        {
            graph.Node("high")["inputs"] = new JsonObject { ["b"] = JsonNode.Parse("0.0005") };
            graph.RemoveWire("high.b");
            graph["parameters"]![0]!["value"] = JsonNode.Parse("123456789.123456789012");
        });

        GraphDefinition first = StrategyGraph.Read(json).Value!;
        GraphDefinition second = StrategyGraph.Read(StrategyGraph.Write(first)).Value!;

        Assert.That(second.Nodes.Single(node => node.Id == "high").Inputs["b"].Number, Is.EqualTo(0.0005m));
        Assert.That(second.Parameters[0].Value.Number, Is.EqualTo(123456789.123456789012m));
        Assert.That(StrategyGraph.Write(second), Does.Contain("0.0005"));
    }

    [Test]
    public void A_duration_is_read_and_written_as_text()
    {
        string json = Graphs.Changed(graph => graph["parameters"]!.AsArray().Add(new JsonObject { ["name"] = "hold", ["type"] = "duration", ["value"] = "4h" }));

        GraphDefinition graph = StrategyGraph.Read(json).Value!;

        Assert.That(graph.Parameters[1].Value.Duration, Is.EqualTo(TimeSpan.FromHours(4)));
        Assert.That(StrategyGraph.Write(graph), Does.Contain("\"4h\""));
    }

    [Test]
    public void Text_that_is_not_JSON_is_refused()
    {
        ReadResult<GraphDefinition> result = StrategyGraph.Read("{ nodes: ");

        Assert.That(result.Errors.Single().Code, Is.EqualTo(GraphErrorCode.NotJson));
    }

    [Test]
    public void An_unknown_field_is_an_error_not_skipped()
    {
        string json = Graphs.Changed(graph => graph.Node("high")["setings"] = new JsonObject());

        ReadResult<GraphDefinition> result = StrategyGraph.Read(json);

        GraphError error = result.Errors.Single();
        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.UnknownField));
        Assert.That(error.Field, Is.EqualTo("nodes[2].setings"));
    }

    [Test]
    public void A_pin_must_be_written_node_dot_pin()
    {
        string json = Graphs.Changed(graph => graph.Wires()[0]!["from"] = "rate");

        ReadResult<GraphDefinition> result = StrategyGraph.Read(json);

        GraphError error = result.Errors.Single();
        Assert.That(error.Code, Is.EqualTo(GraphErrorCode.InvalidField));
        Assert.That(error.Field, Is.EqualTo("wires[0].from"));
    }

    [Test]
    public void Every_form_error_is_reported_at_once()
    {
        string json = Graphs.Changed(graph =>
        {
            graph.Node("rate").Remove("kind");
            graph.Wires()[1]!["to"] = 5;
        });

        ReadResult<GraphDefinition> result = StrategyGraph.Read(json);

        Assert.That(
            result.Errors.Select(error => (error.Code, error.Field)),
            Is.EquivalentTo(
                new[]
                {
                    (GraphErrorCode.MissingField, "nodes[1].kind"),
                    (GraphErrorCode.InvalidField, "wires[1].to")
                }));
    }
}