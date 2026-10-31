using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Validation;

public class GraphValidatorTests
{
    [Test]
    public void Duplicate_node_id_is_an_error()
    {
        Graph graph = new GraphBuilder()
            .Node("ema1", SimpleNodeKind.Candle)
            .Node("ema1", SimpleNodeKind.Candle)
            .Build();

        IReadOnlyList<GraphError> errors = Validate(graph);

        errors.ShouldContain(error => error.Path == "ema1" && error.Item == null);
    }

    [Test]
    public void Wire_between_pins_of_different_types_is_an_error()
    {
        Graph graph = new GraphBuilder()
            .Node("not1", SimpleNodeKind.Not, ("value", Values.Boolean(true)))
            .Node("mul1", SimpleNodeKind.Multiply, ("b", Values.Number(2m)))
            .Wire("not1.result", "mul1.a")
            .Build();

        IReadOnlyList<GraphError> errors = Validate(graph);

        errors.ShouldContain(error => error.Path == "mul1" && error.Item == "a" && error.Reason.Contains("not1.result"));
    }

    [Test]
    public void Second_wire_into_an_input_is_an_error()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("mul1", SimpleNodeKind.Multiply, ("b", Values.Number(2m)))
            .Wire("candle.close", "mul1.a")
            .Wire("candle.open", "mul1.a")
            .Build();

        IReadOnlyList<GraphError> errors = Validate(graph);

        errors.ShouldHaveSingleItem().ShouldBe(new GraphError("mul1", "a", "Input has more than one wire."));
    }

    [TestCase("candle.close", "mul1.c", "mul1", "c")]
    [TestCase("candle.price", "mul1.a", "candle", "price")]
    [TestCase("nowhere.close", "mul1.a", "nowhere", null)]
    [TestCase("candle.close", "nowhere.a", "nowhere", null)]
    public void Wire_to_a_missing_node_or_pin_is_an_error(string from, string to, string path, string? item)
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("mul1", SimpleNodeKind.Multiply, ("a", Values.Number(1m)), ("b", Values.Number(2m)))
            .Wire(from, to)
            .Build();

        IReadOnlyList<GraphError> errors = Validate(graph);

        errors.ShouldContain(error => error.Path == path && error.Item == item);
    }

    [Test]
    public void Value_on_an_unconnected_input_makes_it_set()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("ema1", SimpleNodeKind.Ema, ("period", Values.Number(20m)))
            .Wire("candle.close", "ema1.source")
            .Build();

        Validate(graph).ShouldBeEmpty();
    }

    [Test]
    public void Input_without_a_wire_and_a_value_is_an_error()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("ema1", SimpleNodeKind.Ema)
            .Wire("candle.close", "ema1.source")
            .Build();

        IReadOnlyList<GraphError> errors = Validate(graph);

        errors.ShouldHaveSingleItem().ShouldBe(new GraphError("ema1", "period", "Input is not connected and has no value."));
    }

    [Test]
    public void Value_of_another_type_on_an_input_is_an_error()
    {
        Graph graph = new GraphBuilder()
            .Node("mul1", SimpleNodeKind.Multiply, ("a", Values.Boolean(true)), ("b", Values.Number(2m)))
            .Build();

        IReadOnlyList<GraphError> errors = Validate(graph);

        errors.ShouldHaveSingleItem().ShouldBe(new GraphError("mul1", "a", "Value does not match input type Number."));
    }

    [Test]
    public void Value_on_a_missing_input_is_an_error()
    {
        Graph graph = new GraphBuilder()
            .Node("mul1", SimpleNodeKind.Multiply, ("a", Values.Number(1m)), ("b", Values.Number(2m)), ("c", Values.Number(3m)))
            .Build();

        IReadOnlyList<GraphError> errors = Validate(graph);

        errors.ShouldHaveSingleItem().ShouldBe(new GraphError("mul1", "c", "Node has no input c."));
    }

    [Test]
    public void Cycle_through_two_nodes_is_an_error()
    {
        Graph graph = new GraphBuilder()
            .Node("add1", SimpleNodeKind.Add, ("b", Values.Number(1m)))
            .Node("mul1", SimpleNodeKind.Multiply, ("b", Values.Number(2m)))
            .Wire("add1.result", "mul1.a")
            .Wire("mul1.result", "add1.a")
            .Build();

        IReadOnlyList<GraphError> errors = Validate(graph);

        errors.ShouldHaveSingleItem().ShouldBe(new GraphError("", null, "Wires form a cycle: add1, mul1."));
    }

    [Test]
    public void Duplicate_parameter_name_is_an_error()
    {
        IReadOnlyList<GraphError> errors = Validate(
            new GraphBuilder().Build(),
            new StrategyParameter("fast", PinType.Number, Values.Number(12m)),
            new StrategyParameter("fast", PinType.Number, Values.Number(14m)));

        errors.ShouldHaveSingleItem().ShouldBe(new GraphError("", "fast", "Parameter is declared more than once."));
    }

    [TestCase(1, "Default value must be at least 2.")]
    [TestCase(201, "Default value must be at most 200.")]
    [TestCase(20.5, "Default value must be a whole number.")]
    public void Default_value_outside_the_limits_is_an_error(decimal value, string reason)
    {
        IReadOnlyList<GraphError> errors = Validate(
            new GraphBuilder().Build(),
            new StrategyParameter("fast", PinType.Number, Values.Number(value), Min: 2m, Max: 200m, Integer: true));

        errors.ShouldHaveSingleItem().ShouldBe(new GraphError("", "fast", reason));
    }

    [Test]
    public void Default_value_of_another_type_is_an_error()
    {
        IReadOnlyList<GraphError> errors = Validate(
            new GraphBuilder().Build(),
            new StrategyParameter("interval", PinType.Duration, Values.Number(1m)));

        errors.ShouldHaveSingleItem().ShouldBe(new GraphError("", "interval", "Default value must be of type Duration."));
    }

    [Test]
    public void Duration_parameter_ignores_number_limits()
    {
        IReadOnlyList<GraphError> errors = Validate(
            new GraphBuilder().Build(),
            new StrategyParameter("interval", PinType.Duration, Values.Duration(TimeSpan.FromHours(1)), Min: 2m));

        errors.ShouldBeEmpty();
    }

    [Test]
    public void Parameters_node_gives_the_declared_parameters()
    {
        Graph graph = new GraphBuilder()
            .Node("params", SimpleNodeKind.Parameters)
            .Node("mul1", SimpleNodeKind.Multiply, ("b", Values.Number(2m)))
            .Wire("params.amount", "mul1.a")
            .Build();

        Validate(graph, new StrategyParameter("amount", PinType.Number, Values.Number(100m))).ShouldBeEmpty();
        Validate(graph).ShouldContain(new GraphError("params", "amount", "Node has no output amount."));
    }

    [Test]
    public void Every_error_is_reported_at_once()
    {
        Graph graph = new GraphBuilder()
            .Node("not1", SimpleNodeKind.Not, ("value", Values.Boolean(true)))
            .Node("mul1", SimpleNodeKind.Multiply)
            .Wire("not1.result", "mul1.a")
            .Build();

        IReadOnlyList<GraphError> errors = Validate(graph);

        errors.Select(error => (error.Path, error.Item)).ShouldBe([("mul1", "a"), ("mul1", "b")], ignoreOrder: true);
    }

    [Test]
    public void Node_positions_do_not_affect_validation()
    {
        Graph graph = new GraphBuilder()
            .Node("mul1", SimpleNodeKind.Multiply, ("a", Values.Number(1m)))
            .At("mul1", 10m, 20m)
            .Build();
        Graph moved = graph with { Layout = new Dictionary<string, NodePosition> { ["mul1"] = new NodePosition(-300m, 7.5m) } };

        Validate(moved).ShouldBe(Validate(graph));
    }

    private static IReadOnlyList<GraphError> Validate(Graph graph, params StrategyParameter[] parameters)
    {
        return GraphValidator.ValidateStrategy(StrategyDefinitions.Long(graph, parameters), CompositeCatalog.Empty);
    }
}