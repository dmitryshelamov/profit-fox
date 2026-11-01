using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

public class StrategyRunTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    [Test]
    public void Event_without_order_conditions_gives_no_commands()
    {
        var run = StrategyRun.Start(StrategyDefinitions.Long(new GraphBuilder().Build()), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral);

        run.Handle(new BarClosed(Start, new Bar(1m, 2m, 0.5m, 1.5m, 10m))).ShouldBeEmpty();
    }

    [Test]
    public void Parameter_value_outside_the_limits_is_rejected_before_the_run()
    {
        StrategyDefinition definition = StrategyDefinitions.Long(
            new GraphBuilder().Build(),
            new StrategyParameter("fast", PinType.Number, Values.Number(12m), Min: 2m));

        GraphValidationException exception = Should.Throw<GraphValidationException>(() => StrategyRun.Start(
            definition,
            CompositeCatalog.Empty,
            new Dictionary<string, NodeValue> { ["fast"] = Values.Number(1m), ["slow"] = Values.Number(20m) }, Conditions.Neutral));

        exception.Errors.ShouldBe([new GraphError("", "fast", "Value must be at least 2."), new GraphError("", "slow", "No such parameter.")]);
    }

    [Test]
    public void Candle_gives_the_last_closed_bar_and_nothing_before_it()
    {
        var run = StrategyRun.Start(StrategyDefinitions.Long(new GraphBuilder().Node("candle", SimpleNodeKind.Candle).Build()), CompositeCatalog.Empty, NoValues, Conditions.Neutral);

        run.Handle(new RunStarted(Start));
        run.ValueOf("candle", "close").ShouldBeNull();

        run.Handle(new BarClosed(Start.AddHours(1), new Bar(90m, 110m, 80m, 100m, 5m)));
        run.ValueOf("candle", "open").ShouldBe(Values.Number(90m));
        run.ValueOf("candle", "high").ShouldBe(Values.Number(110m));
        run.ValueOf("candle", "low").ShouldBe(Values.Number(80m));
        run.ValueOf("candle", "close").ShouldBe(Values.Number(100m));
        run.ValueOf("candle", "volume").ShouldBe(Values.Number(5m));
    }

    [TestCase(true, true, true, true, false)]
    [TestCase(true, false, false, true, false)]
    [TestCase(false, false, false, false, true)]
    public void Logic_nodes_combine_booleans(bool a, bool b, bool and, bool or, bool notA)
    {
        Graph graph = new GraphBuilder()
            .Node("and1", SimpleNodeKind.And, ("a", Values.Boolean(a)), ("b", Values.Boolean(b)))
            .Node("or1", SimpleNodeKind.Or, ("a", Values.Boolean(a)), ("b", Values.Boolean(b)))
            .Node("not1", SimpleNodeKind.Not, ("value", Values.Boolean(a)))
            .Build();
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, NoValues, Conditions.Neutral);

        run.Handle(new RunStarted(Start));

        run.ValueOf("and1", "result").ShouldBe(Values.Boolean(and));
        run.ValueOf("or1", "result").ShouldBe(Values.Boolean(or));
        run.ValueOf("not1", "result").ShouldBe(Values.Boolean(notA));
    }

    [Test]
    public void Values_flow_through_wires_in_dependency_order()
    {
        Graph graph = new GraphBuilder()
            .Node("z_mul", SimpleNodeKind.Multiply, ("b", Values.Number(2m)))
            .Node("a_add", SimpleNodeKind.Add, ("b", Values.Number(1m)))
            .Node("candle", SimpleNodeKind.Candle)
            .Node("cmp", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.Greater), ("b", Values.Number(200m)))
            .Wire("candle.close", "a_add.a")
            .Wire("a_add.result", "z_mul.a")
            .Wire("z_mul.result", "cmp.a")
            .Build();
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, NoValues, Conditions.Neutral);

        run.Handle(new BarClosed(Start, new Bar(1m, 1m, 1m, 100m, 1m)));

        run.ValueOf("z_mul", "result").ShouldBe(Values.Number(202m));
        run.ValueOf("cmp", "result").ShouldBe(Values.Boolean(true));
    }

    [Test]
    public void Parameters_node_gives_given_values_and_defaults()
    {
        StrategyDefinition definition = StrategyDefinitions.Long(
            new GraphBuilder().Node("params", SimpleNodeKind.Parameters).Build(),
            new StrategyParameter("fast", PinType.Number, Values.Number(12m)),
            new StrategyParameter("slow", PinType.Number, Values.Number(26m)));
        var run = StrategyRun.Start(definition, CompositeCatalog.Empty, new Dictionary<string, NodeValue> { ["fast"] = Values.Number(5m) }, Conditions.Neutral);

        run.Handle(new RunStarted(Start));

        run.ValueOf("params", "fast").ShouldBe(Values.Number(5m));
        run.ValueOf("params", "slow").ShouldBe(Values.Number(26m));
    }

    [Test]
    public void Composite_node_passes_values_through_its_inner_graph()
    {
        var doubleId = new CustomNodeId(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var doubler = new CompositeDefinition(
            [new PinDeclaration("x", PinType.Number)],
            [new PinDeclaration("y", PinType.Number)],
            new GraphBuilder()
                .Node("in", SimpleNodeKind.Inputs)
                .Node("mul", SimpleNodeKind.Multiply, ("b", Values.Number(2m)))
                .Node("out", SimpleNodeKind.Outputs)
                .Wire("in.x", "mul.a")
                .Wire("mul.result", "out.y")
                .Build());
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Composite("twice", doubleId)
            .Composite("fixed", doubleId, ("x", Values.Number(7m)))
            .Node("add", SimpleNodeKind.Add, ("b", Values.Number(1m)))
            .Wire("candle.close", "twice.x")
            .Wire("twice.y", "add.a")
            .Build();
        var catalog = new CompositeCatalog(new Dictionary<CompositeNodeId, CompositeDefinition> { [doubleId] = doubler });
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), catalog, NoValues, Conditions.Neutral);

        run.Handle(new BarClosed(Start, new Bar(1m, 1m, 1m, 10m, 1m)));

        run.ValueOf("twice/mul", "result").ShouldBe(Values.Number(20m));
        run.ValueOf("add", "result").ShouldBe(Values.Number(21m));
        run.ValueOf("fixed/out", "y").ShouldBe(Values.Number(14m));
    }

    [Test]
    public void Commands_of_one_event_follow_the_node_order()
    {
        Graph graph = new GraphBuilder()
            .Node("b_buy", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Amount), ("when", Values.Boolean(true)), ("size", Values.Number(2m)))
            .Node("a_buy", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Amount), ("when", Values.Boolean(true)), ("size", Values.Number(1m)))
            .Build();
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, NoValues, Conditions.Neutral);

        run.Handle(new RunStarted(Start)).Select(command => (command.OrderId, ((PlaceOrder)command).NodePath)).ShouldBe([("o-1", "a_buy"), ("o-2", "b_buy")]);
    }

    [Test]
    public void Same_events_give_the_same_commands()
    {
        StrategyEvent[] events = [.. Closes(101m, 100m, 99m), new OrderCancelled(Start, "o-1"), .. Closes(98m)];

        IReadOnlyList<OrderCommand> first = Replay(events);
        IReadOnlyList<OrderCommand> second = Replay(events);

        first.ShouldNotBeEmpty();
        second.ShouldBe(first);
    }

    [Test]
    public void Later_bars_do_not_change_earlier_commands()
    {
        StrategyEvent[] common = Closes(150m, 120m);
        IReadOnlyList<OrderCommand> calm = Replay([.. common, .. Closes(120m, 121m)]);
        IReadOnlyList<OrderCommand> wild = Replay([.. common, .. Closes(10m, 400m)]);

        IReadOnlyList<OrderCommand> upToCommon = Replay(common);

        calm.Take(upToCommon.Count).ShouldBe(upToCommon);
        wild.Take(upToCommon.Count).ShouldBe(upToCommon);
    }

    private static StrategyEvent[] Closes(params decimal[] closes)
    {
        return [.. closes.Select((close, index) => new BarClosed(Start.AddHours(index), new Bar(close, close, close, close, 1m)))];
    }

    /// <summary>Holds a limit buy at the last close and returns every command of the replay.</summary>
    private static IReadOnlyList<OrderCommand> Replay(IEnumerable<StrategyEvent> events)
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("hold", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Amount), ("when", Values.Boolean(true)), ("size", Values.Number(100m)))
            .Wire("candle.close", "hold.price")
            .Build();
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, NoValues, Conditions.Neutral);

        return [.. events.SelectMany(run.Handle)];
    }

    private static readonly IReadOnlyDictionary<string, NodeValue> NoValues = new Dictionary<string, NodeValue>();
}