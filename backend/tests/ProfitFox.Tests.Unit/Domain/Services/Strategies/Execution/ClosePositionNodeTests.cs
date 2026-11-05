using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

/// <summary>The node «закрыть позицию» closes the position as its owner does: the orders are cancelled, then the position sells.</summary>
public class ClosePositionNodeTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly IReadOnlyDictionary<string, NodeValue> NoValues = new Dictionary<string, NodeValue>();

    // Scenario "Закрытие лонга".
    [Test]
    public void Close_of_a_long_without_orders_sells_the_position_at_the_market()
    {
        Graph graph = new GraphBuilder().Node("exit", SimpleNodeKind.ClosePosition, ("when", Values.Boolean(true))).Build();
        StrategyRun run = Restore(graph, 0.5m);

        run.Handle(new RunStarted(Start)).ShouldBe([new PlaceOrder("o-1", null, OrderSide.Sell, OrderType.Market, null, 0.5m, null)]);

        run.State.Status.ShouldBe(PositionStatus.Closing);
    }

    // Scenario "Закрытие при стоящей лимитной продаже".
    [Test]
    public void Close_with_a_standing_limit_sell_cancels_it_first_and_sells_when_the_cancel_is_confirmed()
    {
        StrategyRun run = StandingSell();

        run.Handle(Bar(Start.AddHours(1), 90m)).ShouldBe([new CancelOrder("o-1")]);
        run.Handle(Bar(Start.AddHours(2), 90m)).ShouldBeEmpty();
        run.Handle(new OrderCancelled(Start.AddHours(2), "o-1")).ShouldBe([new PlaceOrder("o-2", null, OrderSide.Sell, OrderType.Market, null, 1m, null)]);
        run.Handle(Bar(Start.AddHours(3), 90m)).ShouldBeEmpty();
    }

    // Scenario "Лимитная продажа исполнилась до отмены".
    [Test]
    public void Limit_sell_filled_before_its_cancel_is_confirmed_leaves_nothing_to_sell()
    {
        StrategyRun run = StandingSell();
        run.Handle(Bar(Start.AddHours(1), 90m));

        run.Handle(new OrderFilled(Start.AddHours(1), "o-1", 1m, 101m, IsComplete: true)).ShouldBeEmpty();

        run.State.Closed.ShouldBe(new ClosedPosition(1, Start.AddHours(1), 1m, 0m));
        run.State.Position.ShouldBe(2);
    }

    [Test]
    public void Close_sells_the_position_rounded_down_to_the_quantity_step()
    {
        Graph graph = new GraphBuilder().Node("exit", SimpleNodeKind.ClosePosition, ("when", Values.Boolean(true))).Build();
        var run = StrategyRun.Restore(
            StrategyDefinitions.Long(graph),
            CompositeCatalog.Empty,
            NoValues,
            Conditions.Reference,
            new RunSnapshot(1, 1.0005m, 100.05m, 100.05m, 0m, 0m, 0m, 100m, Start, false, 0, [], 0));

        run.Handle(new RunStarted(Start)).ShouldBe([new PlaceOrder("o-1", null, OrderSide.Sell, OrderType.Market, null, 1m, null)]);
    }

    [Test]
    public void Close_waits_for_a_standing_order_the_venue_cancelled_on_its_own()
    {
        StrategyRun run = StandingSell();
        run.Handle(Bar(Start.AddHours(1), 90m));

        run.Handle(new OrderCancelledByVenue(Start.AddHours(1), "o-1")).ShouldBe([new PlaceOrder("o-2", null, OrderSide.Sell, OrderType.Market, null, 1m, null)]);
    }

    /// <summary>A long position of 1 that holds a limit sell at 101; the node closes it when the close falls to 90.</summary>
    private static StrategyRun StandingSell()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("position", SimpleNodeKind.Position)
            .Node("stop", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.LessOrEqual), ("b", Values.Number(90m)))
            .Node("sell", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Sell, SizeMode: OrderSizeMode.Quantity), ("price", Values.Number(101m)))
            .Node("exit", SimpleNodeKind.ClosePosition)
            .Wire("candle.close", "stop.a")
            .Wire("position.hasPosition", "sell.when")
            .Wire("position.quantity", "sell.size")
            .Wire("stop.result", "exit.when")
            .Build();
        StrategyRun run = Restore(graph, 1m);
        run.Handle(new RunStarted(Start)).ShouldBe([new PlaceOrder("o-1", "sell", OrderSide.Sell, OrderType.Limit, 101m, 1m, null)]);

        return run;
    }

    private static StrategyRun Restore(Graph graph, decimal quantity)
    {
        return StrategyRun.Restore(
            StrategyDefinitions.Long(graph),
            CompositeCatalog.Empty,
            NoValues,
            Conditions.Neutral,
            new RunSnapshot(1, quantity, quantity * 100m, quantity * 100m, 0m, 0m, 0m, 100m, Start, false, 0, [], 0));
    }

    private static BarClosed Bar(DateTimeOffset at, decimal close)
    {
        return new BarClosed(at, new Bar(close, close, close, close, 1m));
    }
}