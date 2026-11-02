using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

/// <summary>Requirements of the positions capability: the current position, its states, its orders and how it closes.</summary>
public class PositionCycleTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly InstrumentRules Rules = new InstrumentRules(0.01m, 0.001m, 0.001m, 0m);

    // Scenario "Старт".
    [Test]
    public void Run_starts_with_position_one_waiting_for_an_entry()
    {
        StrategyRun run = Run(BuyOnce());

        run.Handle(new RunStarted(Start));

        run.State.Position.ShouldBe(1);
        run.State.Status.ShouldBe(PositionStatus.Waiting);
        run.State.Quantity.ShouldBe(0m);
        run.State.Invested.ShouldBe(0m);
    }

    // Scenario "Первый вход".
    [Test]
    public void First_entry_opens_the_position()
    {
        StrategyRun run = Run(BuyOnce());
        run.Handle(new RunStarted(Start));

        run.Handle(new OrderFilled(Start, "o-1", 0.5m, 100m, IsComplete: true));

        run.State.Status.ShouldBe(PositionStatus.Open);
    }

    // Scenario "Тейк-профит лестницы".
    [Test]
    public void Exit_of_the_whole_position_starts_closing_and_cancels_its_orders_once()
    {
        StrategyRun run = Run(Ladder());
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-2", 1m, 100m, IsComplete: true));
        run.Handle(new BarClosed(Start.AddHours(1), new Bar(100m, 100m, 100m, 100m, 1m)));
        run.Handle(new BarClosed(Start.AddHours(2), new Bar(95m, 95m, 95m, 95m, 1m))).ShouldBe([new CancelOrder("o-4")]);

        IReadOnlyList<OrderCommand> commands = run.Handle(new OrderFilled(Start.AddHours(2), "o-3", 1m, 110m, IsComplete: true));

        run.State.Status.ShouldBe(PositionStatus.Closing);
        commands.OfType<CancelOrder>().ShouldBe([new CancelOrder("o-1")]);
    }

    // Scenario "Вход после выхода".
    [Test]
    public void Nodes_place_no_orders_while_the_position_is_closing()
    {
        StrategyRun run = ClosingLadder();

        run.Handle(new BarClosed(Start.AddHours(3), new Bar(95m, 95m, 95m, 95m, 1m))).OfType<PlaceOrder>().ShouldBeEmpty();

        run.SkippedOrders.ShouldBe(0);
    }

    // Scenarios "Отмены подтверждены", "Следующая позиция" and the order of "Вход после выхода".
    [Test]
    public void Position_closes_when_its_last_cancel_is_confirmed_and_the_next_one_takes_new_orders()
    {
        StrategyRun run = ClosingLadder();
        DateTimeOffset confirmed = Start.AddHours(2).AddMinutes(1);
        run.Handle(new OrderCancelled(confirmed, "o-1")).ShouldBeEmpty();
        run.State.Status.ShouldBe(PositionStatus.Closing);

        IReadOnlyList<OrderCommand> commands = run.Handle(new OrderCancelled(confirmed, "o-4"));

        run.State.Closed.ShouldBe(new ClosedPosition(1, confirmed, 10m, 0m));
        run.State.Position.ShouldBe(2);
        run.State.Status.ShouldBe(PositionStatus.Waiting);
        commands.OfType<PlaceOrder>().Select(order => order.NodePath).ShouldBe(["average1", "average2", "enter"], ignoreOrder: true);
    }

    // Scenario "Закрытая позиция не меняется".
    [Test]
    public void Fills_of_the_next_position_count_in_it_alone()
    {
        StrategyRun run = ClosingLadder();
        run.Handle(new OrderCancelled(Start.AddHours(3), "o-1"));
        string entry = run.Handle(new OrderCancelled(Start.AddHours(3), "o-4")).OfType<PlaceOrder>().Single(order => order.NodePath == "enter").OrderId;

        run.Handle(new OrderFilled(Start.AddHours(4), entry, 1m, 100m, IsComplete: true, Fee: 0.1m, FeeAsset: FeeAsset.Quote));

        (run.State.Position, run.State.RealizedPnl, run.State.Fees).ShouldBe((2, 0m, 0.1m));
    }

    [Test]
    public void Closed_position_is_reported_only_on_the_event_that_closed_it()
    {
        StrategyRun run = ClosingLadder();
        run.Handle(new OrderCancelled(Start.AddHours(3), "o-1"));
        run.Handle(new OrderCancelled(Start.AddHours(3), "o-4"));

        run.Handle(new BarClosed(Start.AddHours(4), new Bar(95m, 95m, 95m, 95m, 1m)));

        run.State.Closed.ShouldBeNull();
    }

    // Scenario "Покупка исполнилась до отмены".
    [Test]
    public void Fill_while_closing_stays_in_the_position_and_the_position_sells_it_at_the_market()
    {
        StrategyRun run = ClosingLadder();

        IReadOnlyList<OrderCommand> commands = run.Handle(new OrderFilled(Start.AddHours(3), "o-1", 1m, 90m, IsComplete: true));

        commands.ShouldBe([new PlaceOrder("o-5", null, OrderSide.Sell, OrderType.Market, null, 1m, null)]);
        (run.State.Position, run.State.Status, run.State.Quantity).ShouldBe((1, PositionStatus.Closing, 1m));

        run.Handle(new OrderFilled(Start.AddHours(4), "o-5", 1m, 95m, IsComplete: true)).ShouldBeEmpty();
        run.State.Status.ShouldBe(PositionStatus.Closing);
        run.Handle(new OrderCancelled(Start.AddHours(4), "o-4"));
        run.State.Closed.ShouldBe(new ClosedPosition(1, Start.AddHours(4), 15m, 0m));
    }

    [Test]
    public void Second_fill_while_closing_is_sold_besides_the_sell_already_placed()
    {
        StrategyRun run = ClosingLadder();
        run.Handle(new OrderFilled(Start.AddHours(3), "o-1", 1m, 90m, IsComplete: true));

        IReadOnlyList<OrderCommand> commands = run.Handle(new OrderFilled(Start.AddHours(3), "o-4", 1m, 76m, IsComplete: true));

        commands.ShouldBe([new PlaceOrder("o-6", null, OrderSide.Sell, OrderType.Market, null, 1m, null)]);
    }

    // Scenario "Остаток продаётся при следующем выходе".
    [Test]
    public void Remainder_moves_to_the_next_position_and_its_exit_sells_it()
    {
        StrategyRun run = Run(BuyThenClose());
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true, Fee: 0.0004m, FeeAsset: FeeAsset.Base));
        run.Handle(new OrderFilled(Start, "o-2", 0.999m, 100m, IsComplete: true)).ShouldBe([Buy("o-3")]);
        (run.State.Position, run.State.Quantity).ShouldBe((2, 0.0006m));

        run.Handle(new OrderFilled(Start, "o-3", 1m, 100m, IsComplete: true, Fee: 0.0005m, FeeAsset: FeeAsset.Base))
            .ShouldBe([new PlaceOrder("o-4", "exit", OrderSide.Sell, OrderType.Market, null, 1m, null)]);
        run.State.Quantity.ShouldBe(1.0001m);
        run.Handle(new OrderFilled(Start, "o-4", 1m, 100m, IsComplete: true));

        (run.State.Position, run.State.Quantity).ShouldBe((3, 0.0001m));
    }

    // Scenario "Только остаток" and scenario "Только перенесённый остаток" of the position node.
    [Test]
    public void Position_with_only_a_remainder_waits_for_an_entry_and_the_node_sees_no_position()
    {
        StrategyRun run = Run(BuyThenClose());
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true, Fee: 0.0004m, FeeAsset: FeeAsset.Base));

        run.Handle(new OrderFilled(Start, "o-2", 0.999m, 100m, IsComplete: true));

        run.State.Status.ShouldBe(PositionStatus.Waiting);
        run.ValueOf("position", "hasPosition").ShouldBe(Values.Boolean(false));
        run.ValueOf("position", "quantity").ShouldBe(Values.Number(0m));
        run.ValueOf("position", "invested").ShouldBe(Values.Number(0m));
        run.ValueOf("position", "averagePrice").ShouldBeNull();
    }

    [Test]
    public void Remainder_of_a_short_moves_with_its_collateral()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Sell, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(1.0005m)))
            .Node("exit", SimpleNodeKind.ClosePosition)
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Wire("position.hasPosition", "exit.when")
            .Build();
        var run = StrategyRun.Start(StrategyDefinitions.Short(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral with { Rules = Rules });
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-1", 1.0005m, 100m, IsComplete: true));

        run.Handle(new OrderFilled(Start, "o-2", 1m, 100m, IsComplete: true));

        (run.State.Position, run.State.Quantity, run.State.Collateral).ShouldBe((2, 0.0005m, 0.05m));
    }

    /// <summary>Buys 1 at the market while there is no position and closes the position while there is one.</summary>
    private static Graph BuyThenClose()
    {
        return new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Node("exit", SimpleNodeKind.ClosePosition)
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Wire("position.hasPosition", "exit.when")
            .Build();
    }

    private static PlaceOrder Buy(string id)
    {
        return new PlaceOrder(id, "enter", OrderSide.Buy, OrderType.Market, null, 1m, null);
    }

    /// <summary>The ladder after its take profit sold the whole position, with its averaging buy at 90 still being cancelled.</summary>
    private static StrategyRun ClosingLadder()
    {
        StrategyRun run = Run(Ladder());
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-2", 1m, 100m, IsComplete: true));
        run.Handle(new BarClosed(Start.AddHours(1), new Bar(100m, 100m, 100m, 100m, 1m)));
        run.Handle(new BarClosed(Start.AddHours(2), new Bar(95m, 95m, 95m, 95m, 1m)));
        run.Handle(new OrderFilled(Start.AddHours(2), "o-3", 1m, 110m, IsComplete: true)).OfType<PlaceOrder>().ShouldBeEmpty();

        return run;
    }

    /// <summary>
    ///     Enters 1 at the market while there is no position; holds averaging buys at 90 and at 0.8 of the close whatever the position,
    ///     so they stay after the exit; takes the profit with a limit sell of 1 at 110.
    /// </summary>
    private static Graph Ladder()
    {
        return new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("candle", SimpleNodeKind.Candle)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Node("low", SimpleNodeKind.Multiply, ("b", Values.Number(0.8m)))
            .Node("average1", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("price", Values.Number(90m)), ("size", Values.Number(1m)))
            .Node("average2", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("size", Values.Number(1m)))
            .Node("take", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Sell, SizeMode: OrderSizeMode.Quantity), ("price", Values.Number(110m)), ("size", Values.Number(1m)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Wire("candle.close", "low.a")
            .Wire("low.result", "average2.price")
            .Wire("position.hasPosition", "take.when")
            .Build();
    }

    /// <summary>Buys 0.5 at the market while there is no position.</summary>
    private static Graph BuyOnce()
    {
        return new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(0.5m)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Build();
    }

    private static StrategyRun Run(Graph graph)
    {
        return StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral with { Rules = Rules });
    }
}