using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

/// <summary>No order below the exchange minimum, and a remainder of a position below the minimum quantity closes it.</summary>
public class OrderMinimumTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly InstrumentRules Rules = new InstrumentRules(0.01m, 0.001m, 0.001m, 5m);

    // Scenario "Меньше минимальной суммы".
    [Test]
    public void Limit_buy_below_the_minimum_amount_is_not_placed()
    {
        StrategyRun run = Run(new GraphBuilder()
            .Node("buy", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy, OrderSizeMode.Amount), ("when", Values.Boolean(true)), ("price", Values.Number(50m)), ("size", Values.Number(3m)))
            .Build());

        run.Handle(new RunStarted(Start)).ShouldBeEmpty();
        run.SkippedOrders.ShouldBe(1);
    }

    [Test]
    public void Order_below_the_minimum_quantity_is_not_placed()
    {
        StrategyRun run = Run(new GraphBuilder()
            .Node("buy", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("price", Values.Number(10000m)), ("size", Values.Number(0.0009m)))
            .Build());

        run.Handle(new RunStarted(Start)).ShouldBeEmpty();
        run.SkippedOrders.ShouldBe(1);
    }

    [TestCase(4.99, 0)]
    [TestCase(5, 1)]
    public void Market_order_by_amount_is_checked_against_the_minimum_amount(decimal amount, int placed)
    {
        StrategyRun run = Run(new GraphBuilder()
            .Node("buy", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Amount), ("when", Values.Boolean(true)), ("size", Values.Number(amount)))
            .Build());

        run.Handle(new RunStarted(Start)).Count.ShouldBe(placed);
    }

    // Scenario "Пыль после продажи".
    [Test]
    public void Remainder_below_the_minimum_quantity_after_a_sell_closes_the_position()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Node("exit", SimpleNodeKind.ClosePosition)
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Wire("position.hasPosition", "exit.when")
            .Build();
        StrategyRun run = Run(graph);
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true, Fee: 0.0005m, FeeAsset: FeeAsset.Base))
            .ShouldBe([new PlaceOrder("o-2", "exit", OrderSide.Sell, OrderType.Market, null, 0.999m, null)]);

        run.Handle(new OrderFilled(Start, "o-2", 0.999m, 110m, IsComplete: true))
            .ShouldBe([new PlaceOrder("o-3", "enter", OrderSide.Buy, OrderType.Market, null, 1m, null)]);
        run.ValueOf("position", "hasPosition").ShouldBe(Values.Boolean(false));
        run.ValueOf("position", "quantity").ShouldBe(Values.Number(0m));
        run.ValueOf("position", "invested").ShouldBe(Values.Number(0m));

        run.Handle(new OrderFilled(Start, "o-3", 1m, 120m, IsComplete: true));
        run.ValueOf("position", "quantity").ShouldBe(Values.Number(1m));
        run.ValueOf("position", "averagePrice").ShouldBe(Values.Number(120m));
    }

    private static StrategyRun Run(Graph graph)
    {
        return StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral with { Rules = Rules });
    }

    private static NodeSettings Settings(OrderSide side, OrderSizeMode size)
    {
        return new NodeSettings(Side: side, SizeMode: size);
    }
}