using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Validation;

public class CompositeValidationTests
{
    private static readonly CustomNodeId CrossId = new CustomNodeId(Guid.Parse("00000000-0000-0000-0000-00000000000a"));
    private static readonly CustomNodeId OtherId = new CustomNodeId(Guid.Parse("00000000-0000-0000-0000-00000000000b"));

    [Test]
    public void Declared_output_without_a_wire_inside_is_an_error()
    {
        CompositeDefinition cross = Cross(new GraphBuilder()
            .Node("in", SimpleNodeKind.Inputs)
            .Node("cross", SimpleNodeKind.Cross)
            .Node("out", SimpleNodeKind.Outputs)
            .Wire("in.fast", "cross.a")
            .Wire("in.slow", "cross.b")
            .Wire("cross.up", "out.up")
            .Build());

        IReadOnlyList<GraphError> errors = GraphValidator.ValidateComposite(CrossId, cross, CompositeCatalog.Empty);

        errors.ShouldHaveSingleItem().ShouldBe(new GraphError("out", "down", "Input is not connected and has no value."));
    }

    [Test]
    public void Pin_declared_twice_is_an_error()
    {
        var definition = new CompositeDefinition(
            [new PinDeclaration("x", PinType.Number), new PinDeclaration("x", PinType.Boolean)],
            [new PinDeclaration("on", PinType.Boolean), new PinDeclaration("on", PinType.Boolean)],
            new GraphBuilder().Node("out", SimpleNodeKind.Outputs, ("on", Values.Boolean(true))).Build());

        IReadOnlyList<GraphError> errors = GraphValidator.ValidateComposite(CrossId, definition, CompositeCatalog.Empty);

        errors.ShouldBe([new GraphError("", "x", "Input is declared more than once."), new GraphError("", "on", "Output is declared more than once.")]);
    }

    [Test]
    public void Composite_with_outputs_needs_one_outputs_node()
    {
        Graph withoutOutputs = new GraphBuilder().Node("in", SimpleNodeKind.Inputs).Build();
        Graph withTwo = new GraphBuilder()
            .Node("out1", SimpleNodeKind.Outputs, ("up", Values.Boolean(true)), ("down", Values.Boolean(false)))
            .Node("out2", SimpleNodeKind.Outputs, ("up", Values.Boolean(true)), ("down", Values.Boolean(false)))
            .Build();

        GraphValidator.ValidateComposite(CrossId, Cross(withoutOutputs), CompositeCatalog.Empty)
            .ShouldHaveSingleItem().ShouldBe(new GraphError("", null, "Composite node needs exactly one outputs node."));
        GraphValidator.ValidateComposite(CrossId, Cross(withTwo), CompositeCatalog.Empty)
            .ShouldHaveSingleItem().ShouldBe(new GraphError("", null, "Composite node needs exactly one outputs node."));
    }

    [Test]
    public void Composite_node_in_a_strategy_has_the_declared_pins()
    {
        CompositeCatalog catalog = Catalog((CrossId, ValidCross()));
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Composite("cross1", CrossId)
            .Node("not1", SimpleNodeKind.Not)
            .Wire("candle.close", "cross1.fast")
            .Wire("candle.open", "cross1.slow")
            .Wire("cross1.up", "not1.value")
            .Wire("cross1.sideways", "not1.value")
            .Build();

        IReadOnlyList<GraphError> errors = GraphValidator.ValidateStrategy(StrategyDefinitions.Long(graph), catalog);

        errors.ShouldBe([new GraphError("cross1", "sideways", "Node has no output sideways."), new GraphError("not1", "value", "Input has more than one wire.")]);
    }

    [Test]
    public void Error_inside_a_nested_composite_names_the_path_to_it()
    {
        CompositeDefinition broken = Cross(new GraphBuilder()
            .Node("in", SimpleNodeKind.Inputs)
            .Node("mul2", SimpleNodeKind.Multiply)
            .Node("out", SimpleNodeKind.Outputs, ("up", Values.Boolean(true)), ("down", Values.Boolean(false)))
            .Wire("in.fast", "mul2.a")
            .Build());
        Graph graph = new GraphBuilder().Composite("ladder1", CrossId, ("fast", Values.Number(1m)), ("slow", Values.Number(2m))).Build();

        IReadOnlyList<GraphError> errors = GraphValidator.ValidateStrategy(StrategyDefinitions.Long(graph), Catalog((CrossId, broken)));

        errors.ShouldHaveSingleItem().ShouldBe(new GraphError("ladder1/mul2", "b", "Input is not connected and has no value."));
    }

    [Test]
    public void Composite_that_contains_itself_through_another_is_an_error()
    {
        CompositeDefinition a = Cross(new GraphBuilder()
            .Composite("b1", OtherId, ("fast", Values.Number(1m)), ("slow", Values.Number(2m)))
            .Node("out", SimpleNodeKind.Outputs)
            .Wire("b1.up", "out.up")
            .Wire("b1.down", "out.down")
            .Build());
        CompositeDefinition b = Cross(new GraphBuilder()
            .Composite("a1", CrossId, ("fast", Values.Number(1m)), ("slow", Values.Number(2m)))
            .Node("out", SimpleNodeKind.Outputs)
            .Wire("a1.up", "out.up")
            .Wire("a1.down", "out.down")
            .Build());

        IReadOnlyList<GraphError> errors = GraphValidator.ValidateComposite(OtherId, b, Catalog((CrossId, a), (OtherId, b)));

        errors.ShouldHaveSingleItem().ShouldBe(new GraphError("a1/b1", null, $"Composite node contains itself: {OtherId.Value} → {CrossId.Value} → {OtherId.Value}."));
    }

    [Test]
    public void Reference_to_a_missing_composite_is_an_error()
    {
        Graph graph = new GraphBuilder().Composite("cross1", CrossId).Build();

        IReadOnlyList<GraphError> errors = GraphValidator.ValidateStrategy(StrategyDefinitions.Long(graph), CompositeCatalog.Empty);

        errors.ShouldHaveSingleItem().ShouldBe(new GraphError("cross1", null, "No such composite node."));
    }

    private static CompositeDefinition ValidCross()
    {
        return Cross(new GraphBuilder()
            .Node("in", SimpleNodeKind.Inputs)
            .Node("cross", SimpleNodeKind.Cross)
            .Node("out", SimpleNodeKind.Outputs)
            .Wire("in.fast", "cross.a")
            .Wire("in.slow", "cross.b")
            .Wire("cross.up", "out.up")
            .Wire("cross.down", "out.down")
            .Build());
    }

    private static CompositeDefinition Cross(Graph graph)
    {
        return new CompositeDefinition(
            [new PinDeclaration("fast", PinType.Number), new PinDeclaration("slow", PinType.Number)],
            [new PinDeclaration("up", PinType.Boolean), new PinDeclaration("down", PinType.Boolean)],
            graph);
    }

    private static CompositeCatalog Catalog(params (CompositeNodeId Id, CompositeDefinition Definition)[] composites)
    {
        return new CompositeCatalog(composites.ToDictionary(composite => composite.Id, composite => composite.Definition));
    }
}