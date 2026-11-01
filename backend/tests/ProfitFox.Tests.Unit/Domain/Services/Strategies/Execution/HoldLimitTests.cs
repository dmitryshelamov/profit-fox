using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

public class HoldLimitTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    [Test]
    public void Limit_order_is_placed_at_the_price_and_size()
    {
        StrategyRun run = BuyAtClose(OrderSizeMode.Quantity, 1m);

        Close(run, 101m).ShouldBe([Limit("o-1", OrderSide.Buy, 101m, 1m)]);
        Close(run, 101m).ShouldBeEmpty();
    }

    [Test]
    public void Size_by_amount_becomes_a_quantity_at_the_price()
    {
        StrategyRun run = BuyAtClose(OrderSizeMode.Amount, 100m);

        Close(run, 50m).ShouldBe([Limit("o-1", OrderSide.Buy, 50m, 2m)]);
    }

    [Test]
    public void Changed_price_cancels_the_order_and_places_a_new_one_after_the_cancel()
    {
        StrategyRun run = BuyAtClose(OrderSizeMode.Quantity, 1m);
        Close(run, 101m);

        Close(run, 100m).ShouldBe([new CancelOrder("o-1")]);
        Close(run, 99m).ShouldBeEmpty();
        run.Handle(new OrderCancelled(Start, "o-1")).ShouldBe([Limit("o-2", OrderSide.Buy, 99m, 1m)]);
    }

    [Test]
    public void False_condition_cancels_the_order()
    {
        StrategyRun run = BuyAtClose(OrderSizeMode.Quantity, 1m);
        Close(run, 150m);

        Close(run, 300m).ShouldBe([new CancelOrder("o-1")]);
    }

    [Test]
    public void Missing_price_cancels_the_order()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("price", SimpleNodeKind.Divide, ("a", Values.Number(100m)))
            .Node("hold", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("size", Values.Number(1m)))
            .Wire("candle.close", "price.b")
            .Wire("price.result", "hold.price")
            .Build();
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, NoValues, Conditions.Neutral);
        Close(run, 4m);

        Close(run, 0m).ShouldBe([new CancelOrder("o-1")]);
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void Price_that_is_not_positive_holds_no_order(decimal close)
    {
        StrategyRun amount = BuyAtClose(OrderSizeMode.Amount, 100m);
        StrategyRun quantity = BuyAtClose(OrderSizeMode.Quantity, 1m);

        Close(amount, close).ShouldBeEmpty();
        Close(quantity, close).ShouldBeEmpty();
    }

    [Test]
    public void Size_that_is_not_positive_holds_no_order()
    {
        StrategyRun run = BuyAtClose(OrderSizeMode.Quantity, 0m);

        Close(run, 100m).ShouldBeEmpty();
    }

    [Test]
    public void Missing_size_holds_no_order()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("hold", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("price", Values.Number(100m)))
            .Wire("position.averagePrice", "hold.size")
            .Build();
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, NoValues, Conditions.Neutral);

        run.Handle(new RunStarted(Start)).ShouldBeEmpty();
    }

    [Test]
    public void Fill_of_an_order_being_cancelled_still_counts()
    {
        StrategyRun run = BuyAtClose(OrderSizeMode.Quantity, 1m);
        Close(run, 101m);
        Close(run, 100m);

        run.Handle(new OrderFilled(Start, "o-1", 1m, 101m, IsComplete: true)).ShouldBe([Limit("o-2", OrderSide.Buy, 100m, 1m)]);
        run.ValueOf("position", "quantity").ShouldBe(Values.Number(1m));
    }

    [Test]
    public void Partial_fill_leaves_a_remainder_that_no_longer_matches_the_size()
    {
        StrategyRun run = BuyAtClose(OrderSizeMode.Quantity, 1m);
        Close(run, 101m);

        run.Handle(new OrderFilled(Start, "o-1", 0.4m, 101m, IsComplete: false)).ShouldBe([new CancelOrder("o-1")]);
    }

    [Test]
    public void Exit_limit_order_follows_the_position()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("size", Values.Number(2m)))
            .Node("hold", SimpleNodeKind.HoldLimit, Settings(OrderSide.Sell, OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("price", Values.Number(110m)), ("size", Values.Number(5m)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Build();
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, NoValues, Conditions.Neutral);

        run.Handle(new RunStarted(Start)).ShouldBe([new PlaceOrder("o-1", "enter", OrderSide.Buy, OrderType.Market, null, 2m, null)]);
        run.Handle(new OrderFilled(Start, "o-1", 2m, 100m, IsComplete: true)).ShouldBe([Limit("o-2", OrderSide.Sell, 110m, 2m, "hold")]);
    }

    /// <summary>Holds a buy at the last close while the close is below 200.</summary>
    private static StrategyRun BuyAtClose(OrderSizeMode mode, decimal size)
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("position", SimpleNodeKind.Position)
            .Node("cheap", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.Less), ("b", Values.Number(200m)))
            .Node("hold", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy, mode), ("size", Values.Number(size)))
            .Wire("candle.close", "cheap.a")
            .Wire("cheap.result", "hold.when")
            .Wire("candle.close", "hold.price")
            .Build();

        return StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, NoValues, Conditions.Neutral);
    }

    private static IReadOnlyList<OrderCommand> Close(StrategyRun run, decimal close)
    {
        return run.Handle(new BarClosed(Start, new Bar(close, close, close, close, 1m)));
    }

    private static PlaceOrder Limit(string id, OrderSide side, decimal price, decimal quantity, string node = "hold")
    {
        return new PlaceOrder(id, node, side, OrderType.Limit, price, quantity, null);
    }

    private static NodeSettings Settings(OrderSide side, OrderSizeMode size)
    {
        return new NodeSettings(Side: side, SizeMode: size);
    }

    private static readonly IReadOnlyDictionary<string, NodeValue> NoValues = new Dictionary<string, NodeValue>();
}