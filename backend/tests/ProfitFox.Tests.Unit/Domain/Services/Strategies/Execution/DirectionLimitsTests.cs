using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

public class DirectionLimitsTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    [TestCase(StrategyDirection.Long, OrderSide.Sell)]
    [TestCase(StrategyDirection.Short, OrderSide.Buy)]
    public void Exit_order_without_a_position_is_not_placed(StrategyDirection direction, OrderSide exit)
    {
        Graph graph = new GraphBuilder()
            .Node("exit", SimpleNodeKind.MarketOrder, Settings(exit, OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("size", Values.Number(1m)))
            .Build();
        var run = StrategyRun.Start(new StrategyDefinition(direction, [], graph), CompositeCatalog.Empty, NoValues);

        run.Handle(new RunStarted(Start)).ShouldBeEmpty();
    }

    [TestCase(StrategyDirection.Long, OrderSide.Buy, OrderSide.Sell)]
    [TestCase(StrategyDirection.Short, OrderSide.Sell, OrderSide.Buy)]
    public void Exit_order_is_limited_by_the_position(StrategyDirection direction, OrderSide entry, OrderSide exit)
    {
        StrategyRun run = EnterFourThenExit(direction, entry, Settings(exit, OrderSizeMode.Quantity), 10m);
        run.Handle(new RunStarted(Start));

        run.Handle(new OrderFilled(Start, "o-1", 4m, 100m, IsComplete: true))
            .ShouldBe([new PlaceOrder("o-2", "exit", exit, OrderType.Market, null, 4m, null)]);
    }

    [Test]
    public void Exit_order_by_amount_is_converted_at_the_last_close()
    {
        StrategyRun run = EnterFourThenExit(StrategyDirection.Long, OrderSide.Buy, Settings(OrderSide.Sell, OrderSizeMode.Amount), 200m);
        run.Handle(new BarClosed(Start, new Bar(100m, 100m, 100m, 100m, 1m)));

        run.Handle(new OrderFilled(Start, "o-1", 4m, 100m, IsComplete: true))
            .ShouldBe([new PlaceOrder("o-2", "exit", OrderSide.Sell, OrderType.Market, null, 2m, null)]);
    }

    [Test]
    public void Exit_order_by_amount_needs_a_positive_close()
    {
        StrategyRun run = EnterFourThenExit(StrategyDirection.Long, OrderSide.Buy, Settings(OrderSide.Sell, OrderSizeMode.Amount), 200m);
        run.Handle(new BarClosed(Start, new Bar(0m, 0m, 0m, 0m, 1m)));

        run.Handle(new OrderFilled(Start, "o-1", 4m, 100m, IsComplete: true)).ShouldBeEmpty();
    }

    [Test]
    public void Exit_order_by_amount_waits_for_a_closed_bar()
    {
        StrategyRun run = EnterFourThenExit(StrategyDirection.Long, OrderSide.Buy, Settings(OrderSide.Sell, OrderSizeMode.Amount), 200m);
        run.Handle(new RunStarted(Start));

        run.Handle(new OrderFilled(Start, "o-1", 4m, 100m, IsComplete: true)).ShouldBeEmpty();
    }

    [TestCase(StrategyDirection.Long, OrderSide.Buy, OrderSide.Sell)]
    [TestCase(StrategyDirection.Short, OrderSide.Sell, OrderSide.Buy)]
    public void Close_position_exits_the_whole_position_once(StrategyDirection direction, OrderSide entry, OrderSide exit)
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, Settings(entry, OrderSizeMode.Quantity), ("size", Values.Number(0.5m)))
            .Node("close", SimpleNodeKind.ClosePosition)
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Wire("position.hasPosition", "close.when")
            .Build();
        var run = StrategyRun.Start(new StrategyDefinition(direction, [], graph), CompositeCatalog.Empty, NoValues);
        run.Handle(new RunStarted(Start)).ShouldBe([new PlaceOrder("o-1", "enter", entry, OrderType.Market, null, 0.5m, null)]);

        run.Handle(new OrderFilled(Start, "o-1", 0.5m, 100m, IsComplete: true))
            .ShouldBe([new PlaceOrder("o-2", "close", exit, OrderType.Market, null, 0.5m, null)]);
        run.Handle(new BarClosed(Start, new Bar(1m, 1m, 1m, 1m, 1m))).ShouldBeEmpty();
    }

    [Test]
    public void Exit_fill_beyond_the_position_leaves_no_position()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("size", Values.Number(2m)))
            .Node("take", SimpleNodeKind.HoldLimit, Settings(OrderSide.Sell, OrderSizeMode.Quantity), ("price", Values.Number(110m)))
            .Node("stop", SimpleNodeKind.ClosePosition)
            .Node("cheap", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.Less), ("b", Values.Number(90m)))
            .Node("candle", SimpleNodeKind.Candle)
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Wire("position.hasPosition", "take.when")
            .Wire("position.quantity", "take.size")
            .Wire("candle.close", "cheap.a")
            .Wire("cheap.result", "stop.when")
            .Build();
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, NoValues);
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-1", 2m, 100m, IsComplete: true));
        run.Handle(new BarClosed(Start, new Bar(85m, 85m, 85m, 85m, 1m)));
        run.Handle(new OrderFilled(Start, "o-3", 2m, 85m, IsComplete: true));

        run.Handle(new OrderFilled(Start, "o-2", 2m, 110m, IsComplete: true));

        run.ValueOf("position", "quantity").ShouldBe(Values.Number(0m));
        run.ValueOf("position", "invested").ShouldBe(Values.Number(0m));
    }

    private static StrategyRun EnterFourThenExit(StrategyDirection direction, OrderSide entry, NodeSettings exit, decimal exitSize)
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, Settings(entry, OrderSizeMode.Quantity), ("size", Values.Number(4m)))
            .Node("exit", SimpleNodeKind.MarketOrder, exit, ("size", Values.Number(exitSize)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Wire("position.hasPosition", "exit.when")
            .Build();

        return StrategyRun.Start(new StrategyDefinition(direction, [], graph), CompositeCatalog.Empty, NoValues);
    }

    private static NodeSettings Settings(OrderSide side, OrderSizeMode size)
    {
        return new NodeSettings(Side: side, SizeMode: size);
    }

    private static readonly IReadOnlyDictionary<string, NodeValue> NoValues = new Dictionary<string, NodeValue>();
}