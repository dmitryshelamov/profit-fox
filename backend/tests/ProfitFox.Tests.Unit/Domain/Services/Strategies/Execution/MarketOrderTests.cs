using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

public class MarketOrderTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    [Test]
    public void Market_order_by_amount_is_placed_when_its_condition_holds()
    {
        StrategyRun run = Run(new GraphBuilder()
            .Node("buy", SimpleNodeKind.MarketOrder, Buy(OrderSizeMode.Amount), ("when", Values.Boolean(true)), ("size", Values.Number(100m)))
            .Build());

        run.Handle(new RunStarted(Start)).ShouldBe([new PlaceOrder("o-1", "buy", OrderSide.Buy, OrderType.Market, null, null, 100m)]);
    }

    [Test]
    public void Market_order_by_quantity_carries_the_quantity()
    {
        StrategyRun run = Run(new GraphBuilder()
            .Node("buy", SimpleNodeKind.MarketOrder, Buy(OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("size", Values.Number(0.5m)))
            .Build());

        run.Handle(new RunStarted(Start)).ShouldBe([new PlaceOrder("o-1", "buy", OrderSide.Buy, OrderType.Market, null, 0.5m, null)]);
    }

    [Test]
    public void Market_order_is_not_repeated_until_the_order_is_finished()
    {
        StrategyRun run = Run(new GraphBuilder()
            .Node("buy", SimpleNodeKind.MarketOrder, Buy(OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("size", Values.Number(2m)))
            .Build());
        run.Handle(new RunStarted(Start));

        run.Handle(new BarClosed(Start, Bar(100m))).ShouldBeEmpty();
        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: false)).ShouldBeEmpty();
        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true))
            .ShouldBe([new PlaceOrder("o-2", "buy", OrderSide.Buy, OrderType.Market, null, 2m, null)]);
    }

    [Test]
    public void Cancelled_market_order_is_finished()
    {
        StrategyRun run = Run(new GraphBuilder()
            .Node("buy", SimpleNodeKind.MarketOrder, Buy(OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("size", Values.Number(2m)))
            .Build());
        run.Handle(new RunStarted(Start));

        run.Handle(new OrderCancelled(Start, "o-1")).ShouldBe([new PlaceOrder("o-2", "buy", OrderSide.Buy, OrderType.Market, null, 2m, null)]);
    }

    [Test]
    public void Market_order_without_a_size_is_not_placed()
    {
        StrategyRun run = Run(new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("buy", SimpleNodeKind.MarketOrder, Buy(OrderSizeMode.Quantity), ("when", Values.Boolean(true)))
            .Wire("candle.close", "buy.size")
            .Build());

        run.Handle(new RunStarted(Start)).ShouldBeEmpty();
    }

    [Test]
    public void Market_order_of_a_size_that_is_not_positive_is_not_placed()
    {
        StrategyRun run = Run(new GraphBuilder()
            .Node("buy", SimpleNodeKind.MarketOrder, Buy(OrderSizeMode.Amount), ("when", Values.Boolean(true)), ("size", Values.Number(0m)))
            .Build());

        run.Handle(new RunStarted(Start)).ShouldBeEmpty();
    }

    [Test]
    public void Market_order_is_not_placed_while_its_condition_is_false()
    {
        StrategyRun run = Run(new GraphBuilder()
            .Node("buy", SimpleNodeKind.MarketOrder, Buy(OrderSizeMode.Quantity), ("when", Values.Boolean(false)), ("size", Values.Number(1m)))
            .Build());

        run.Handle(new RunStarted(Start)).ShouldBeEmpty();
    }

    [Test]
    public void Candle_on_an_order_event_is_the_last_closed_bar()
    {
        StrategyRun run = Run(new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("buy", SimpleNodeKind.MarketOrder, Buy(OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("size", Values.Number(1m)))
            .Build());
        run.Handle(new BarClosed(Start, Bar(100m)));

        run.Handle(new OrderFilled(Start, "o-1", 1m, 101m, IsComplete: true));

        run.ValueOf("candle", "close").ShouldBe(Values.Number(100m));
    }

    private static NodeSettings Buy(OrderSizeMode size)
    {
        return new NodeSettings(Side: OrderSide.Buy, SizeMode: size);
    }

    private static Bar Bar(decimal close)
    {
        return new Bar(close, close, close, close, 1m);
    }

    private static StrategyRun Run(Graph graph)
    {
        return StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>());
    }
}