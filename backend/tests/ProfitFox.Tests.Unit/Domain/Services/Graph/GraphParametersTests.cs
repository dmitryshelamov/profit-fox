using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Tests.Unit.Domain.Services.Graph;

/// <summary>
///     Change add-strategy-backtest, task 3.1 (design.md, decision 3): a copy of the graph with other values of its
///     parameters, read by the type of the parameter, and checked by the checks of the parameters as they always were.
/// </summary>
public sealed class GraphParametersTests
{
    private static readonly GraphDefinition Ladder = StrategyGraph.ReadStrategy(Graphs.Ladder).Value!.Source;

    private static readonly GraphDefinition Bounded = StrategyGraph.ReadForm(System.Text.Json.JsonDocument.Parse(
        Graphs.ChangedStrategy(graph =>
        {
            graph["parameters"]![0]!["minimum"] = 0;
            graph["parameters"]![0]!["maximum"] = 0.01;
            graph["parameters"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["name"] = "after", ["type"] = "duration", ["value"] = "4h" });
        })).RootElement).Value!;

    [Test]
    public void The_named_parameters_take_the_new_values_and_the_others_keep_theirs()
    {
        List<GraphError> errors = [];

        GraphDefinition changed = GraphParameters.With(Bounded, [new("threshold", "0.001"), new("after", "1d")], errors);

        Assert.That(errors, Is.Empty);
        Assert.That(changed.Parameters.Select(parameter => parameter.Value), Is.EqualTo(new[] { GraphValue.FromNumber(0.001m), GraphValue.FromDuration(TimeSpan.FromDays(1)) }));
        Assert.That(GraphParameters.Check(changed), Is.Empty);
        Assert.That(Bounded.Parameters[0].Value, Is.EqualTo(GraphValue.FromNumber(0.0005m)), "the graph itself does not change");
        Assert.That(changed.Nodes, Is.SameAs(Bounded.Nodes));
    }

    [Test]
    public void Without_values_the_graph_is_the_same()
    {
        List<GraphError> errors = [];

        GraphDefinition same = GraphParameters.With(Ladder, [], errors);

        Assert.That(same, Is.SameAs(Ladder));
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void A_value_outside_the_bounds_gives_the_error_of_the_checks_of_parameters()
    {
        List<GraphError> errors = [];

        GraphDefinition changed = GraphParameters.With(Bounded, [new("threshold", "0.02")], errors);

        Assert.That(errors, Is.Empty, "the text is a number");
        GraphError error = GraphParameters.Check(changed).Single();
        Assert.That((error.Code, error.Parameter), Is.EqualTo((GraphErrorCode.ParameterOutOfBounds, (string?)"threshold")));
        Assert.That(StrategyGraph.ReadStrategy(StrategyGraph.Write(changed)).Errors.Select(item => item.Code), Is.EqualTo(new[] { GraphErrorCode.ParameterOutOfBounds }));
    }

    [Test]
    public void A_value_of_another_type_gives_the_error_of_the_checks_of_parameters()
    {
        List<GraphError> errors = [];

        GraphDefinition changed = GraphParameters.With(Bounded, [new("threshold", "4h"), new("after", "0.5")], errors);

        Assert.That(errors, Is.Empty, "both texts are values");
        Assert.That(
            GraphParameters.Check(changed).Select(error => (error.Code, error.Parameter)),
            Is.EqualTo(new[] { (GraphErrorCode.ParameterTypeMismatch, (string?)"threshold"), (GraphErrorCode.ParameterTypeMismatch, "after") }));
    }

    [Test]
    public void A_text_that_is_no_value_is_a_mismatch_of_the_type()
    {
        List<GraphError> errors = [];

        GraphDefinition changed = GraphParameters.With(Bounded, [new("threshold", "abc"), new("after", "")], errors);

        Assert.That(errors.Select(error => (error.Code, error.Parameter)), Is.EqualTo(new[] { (GraphErrorCode.ParameterTypeMismatch, (string?)"threshold"), (GraphErrorCode.ParameterTypeMismatch, "after") }));
        Assert.That(changed.Parameters.Select(parameter => parameter.Value), Is.EqualTo(Bounded.Parameters.Select(parameter => parameter.Value)));
    }

    [Test]
    public void An_unknown_name_is_an_error()
    {
        List<GraphError> errors = [];

        GraphParameters.With(Ladder, [new("limitDepositt", "1000")], errors);

        GraphError error = errors.Single();
        Assert.That((error.Code, error.Parameter), Is.EqualTo((GraphErrorCode.UnknownParameter, (string?)"limitDepositt")));
    }

    [TestCase("0.0005", 0.0005)]
    [TestCase("1e-3", 0.001)]
    [TestCase("100", 100)]
    public void A_number_is_read_as_the_json_of_a_graph_writes_it(string text, double expected)
    {
        Assert.That(GraphParameters.TryRead(text, out GraphValue value));
        Assert.That(value, Is.EqualTo(GraphValue.FromNumber((decimal)expected)));
    }

    [Test]
    public void A_boolean_and_a_duration_are_read_too()
    {
        Assert.That(GraphParameters.TryRead("true", out GraphValue yes) && yes == GraphValue.FromBoolean(true));
        Assert.That(GraphParameters.TryRead("4h", out GraphValue time) && time == GraphValue.FromDuration(TimeSpan.FromHours(4)));
        Assert.That(GraphParameters.TryRead(" 1", out _), Is.True, "JSON allows spaces around a number");
        Assert.That(GraphParameters.TryRead("+1", out _), Is.False, "JSON has no plus");
    }
}
