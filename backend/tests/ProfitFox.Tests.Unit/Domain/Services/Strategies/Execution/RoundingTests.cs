using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

/// <summary>Orders go out on the steps of the instrument: quantities down, buy prices down and sell prices up.</summary>
public class RoundingTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly InstrumentRules Rules = new InstrumentRules(0.01m, 0.001m, 0m, 0m);

    // Scenario "Округление докупки".
    [Test]
    public void Limit_buy_by_amount_goes_out_at_the_price_rounded_down_and_the_quantity_rounded_down()
    {
        StrategyRun run = HoldAtClose(OrderSide.Buy, OrderSizeMode.Amount, 100m);

        Close(run, 99.004m).ShouldBe([Limit("o-1", OrderSide.Buy, 99m, 1.010m)]);
    }

    // Scenario "Округление продажи вверх".
    [Test]
    public void Limit_sell_goes_out_at_the_price_rounded_up()
    {
        StrategyRun run = HoldAtClose(OrderSide.Sell, OrderSizeMode.Quantity, 1.0009m);
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-1", 2m, 100m, IsComplete: true));

        Close(run, 101.001m).ShouldBe([Limit("o-2", OrderSide.Sell, 101.01m, 1m)]);
    }

    // Scenario "Изменение меньше шага цены".
    [Test]
    public void Price_change_below_the_tick_keeps_the_order()
    {
        StrategyRun run = HoldAtClose(OrderSide.Buy, OrderSizeMode.Quantity, 1m);
        Close(run, 98.01m).ShouldBe([Limit("o-1", OrderSide.Buy, 98.01m, 1m)]);

        Close(run, 98.0149m).ShouldBeEmpty();
    }

    [Test]
    public void Exit_takes_at_most_the_position_rounded_down()
    {
        StrategyRun run = HoldAtClose(OrderSide.Sell, OrderSizeMode.Quantity, 5m);
        run.Handle(new RunStarted(Start));

        run.Handle(new OrderFilled(Start, "o-1", 1.0009m, 100m, IsComplete: true));

        Close(run, 110m).ShouldBe([Limit("o-2", OrderSide.Sell, 110m, 1m)]);
    }

    [Test]
    public void Market_orders_go_out_with_the_quantity_rounded_down_and_the_amount_as_it_is()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("byAmount", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Amount), ("size", Values.Number(100.0001m)))
            .Node("byQuantity", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("size", Values.Number(0.0019m)))
            .Node("sellPart", SimpleNodeKind.MarketOrder, Settings(OrderSide.Sell, OrderSizeMode.Amount), ("size", Values.Number(0.299m)))
            .Node("close", SimpleNodeKind.ClosePosition)
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "byAmount.when")
            .Wire("flat.result", "byQuantity.when")
            .Wire("position.hasPosition", "sellPart.when")
            .Wire("position.hasPosition", "close.when")
            .Build();
        StrategyRun run = Run(graph);

        run.Handle(new RunStarted(Start)).ShouldBe(
        [
            new PlaceOrder("o-1", "byAmount", OrderSide.Buy, OrderType.Market, null, null, 100.0001m),
            new PlaceOrder("o-2", "byQuantity", OrderSide.Buy, OrderType.Market, null, 0.001m, null)
        ]);
        run.Handle(new BarClosed(Start, new Bar(100m, 100m, 100m, 100m, 1m)));

        run.Handle(new OrderFilled(Start, "o-1", 1.0005m, 100m, IsComplete: true)).ShouldBe(
        [
            new PlaceOrder("o-3", "close", OrderSide.Sell, OrderType.Market, null, 1m, null),
            new PlaceOrder("o-4", "sellPart", OrderSide.Sell, OrderType.Market, null, 0.002m, null)
        ]);
    }

    private static StrategyRun HoldAtClose(OrderSide side, OrderSizeMode mode, decimal size)
    {
        GraphBuilder builder = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("hold", SimpleNodeKind.HoldLimit, Settings(side, mode), ("when", Values.Boolean(true)), ("size", Values.Number(size)))
            .Wire("candle.close", "hold.price");

        if (side == OrderSide.Sell)
        {
            builder.Node("position", SimpleNodeKind.Position)
                .Node("enter", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("size", Values.Number(1m)))
                .Node("flat", SimpleNodeKind.Not)
                .Wire("position.hasPosition", "flat.value")
                .Wire("flat.result", "enter.when");
        }

        return Run(builder.Build());
    }

    private static StrategyRun Run(Graph graph)
    {
        return StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral with { Rules = Rules });
    }

    private static IReadOnlyList<OrderCommand> Close(StrategyRun run, decimal close)
    {
        return run.Handle(new BarClosed(Start, new Bar(close, close, close, close, 1m)));
    }

    private static PlaceOrder Limit(string id, OrderSide side, decimal price, decimal quantity)
    {
        return new PlaceOrder(id, "hold", side, OrderType.Limit, price, quantity, null);
    }

    private static NodeSettings Settings(OrderSide side, OrderSizeMode size)
    {
        return new NodeSettings(Side: side, SizeMode: size);
    }
}