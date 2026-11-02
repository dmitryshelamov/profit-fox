using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

/// <summary>A run restored from its snapshot goes on as the run it was taken from.</summary>
public class RunSnapshotTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly IReadOnlyDictionary<string, NodeValue> NoValues = new Dictionary<string, NodeValue>();

    // Scenario "Лимитка после рестарта".
    [Test]
    public void Restored_limit_order_belongs_to_its_node()
    {
        StrategyRun run = Restore(
            StrategyDefinitions.Long(EnterAndHoldExit()),
            Position(4, quantity: 1m, invested: 100m) with { Orders = [new RunOrder("o-7", "exit", OrderSide.Sell, 101m, 1m)], LastOrderNumber = 7 });

        Close(run, 100m).ShouldBeEmpty();
    }

    // Scenario "Номер позиции".
    [Test]
    public void Positions_go_on_from_the_restored_number()
    {
        StrategyRun run = Restore(
            StrategyDefinitions.Long(EnterAndHoldExit()),
            Position(4, quantity: 1m, invested: 100m) with { Orders = [new RunOrder("o-7", "exit", OrderSide.Sell, 101m, 1m)], LastOrderNumber = 7 });

        run.Handle(new OrderFilled(Start, "o-7", 1m, 101m, IsComplete: true)).ShouldBe([new PlaceOrder("o-8", "enter", OrderSide.Buy, OrderType.Market, null, 1m, null)]);
        run.Position.ShouldBe(5);
    }

    // The market exit of a closing position has no node: it is the position's own order.
    [Test]
    public void Restored_closing_position_closes_with_its_own_exit()
    {
        StrategyRun run = Restore(
            StrategyDefinitions.Long(EnterAndHoldExit()),
            Position(3, quantity: 0.5m, invested: 50m) with { Closing = true, Orders = [new RunOrder("o-9", null, OrderSide.Sell, null, 0.5m)], LastOrderNumber = 9 });

        run.Handle(new OrderFilled(Start, "o-9", 0.5m, 100m, IsComplete: true)).ShouldBe([new PlaceOrder("o-10", "enter", OrderSide.Buy, OrderType.Market, null, 1m, null)]);
        run.Position.ShouldBe(4);
    }

    // Scenario "Закрытие лонга": the owner closes the position of a stopped bot through the position's own exit.
    [Test]
    public void Closed_on_request_the_position_sells_itself_at_the_market()
    {
        StrategyRun run = Restore(StrategyDefinitions.Long(EnterAndHoldExit()), Position(2, quantity: 0.5m, invested: 50m) with { LastOrderNumber = 4 });

        run.Close(Start).ShouldBe([new PlaceOrder("o-5", null, OrderSide.Sell, OrderType.Market, null, 0.5m, null)]);
        run.Record(new OrderFilled(Start, "o-5", 0.5m, 110m, IsComplete: true)).ShouldBeEmpty();
        run.State.Closed!.Number.ShouldBe(2);
        run.Position.ShouldBe(3);
    }

    [Test]
    public void Order_numbers_go_on_from_the_restored_one()
    {
        StrategyRun run = Restore(StrategyDefinitions.Long(EnterAndHoldExit()), Position(1, quantity: 0m, invested: 0m) with { LastOrderNumber = 12 });

        run.Handle(new RunStarted(Start)).ShouldBe([new PlaceOrder("o-13", "enter", OrderSide.Buy, OrderType.Market, null, 1m, null)]);
    }

    // The money of the bot is not stored: it comes from the balance and what the positions made, so a new balance counts at once.
    [TestCase(StrategyDirection.Long)]
    [TestCase(StrategyDirection.Short)]
    public void Snapshot_after_fills_restores_the_same_state(StrategyDirection direction)
    {
        bool isLong = direction == StrategyDirection.Long;
        StrategyDefinition definition = isLong
            ? StrategyDefinitions.Long(EnterAndHoldExit())
            : StrategyDefinitions.Short(EnterAndHoldExit(OrderSide.Sell, OrderSide.Buy, 0.99m));
        var conditions = new TradingConditions(1000m, Conditions.Reference.Rules, new FeeSchedule(0.001m, 0.002m, MarketCategory.Linear));
        var run = StrategyRun.Start(definition, CompositeCatalog.Empty, NoValues, conditions);
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true, Fee: 0.2m));
        run.Handle(new OrderFilled(Start, "o-2", 1m, isLong ? 101m : 99m, IsComplete: true, Fee: 0.1m));
        run.Handle(new OrderFilled(Start.AddHours(1), "o-3", 1m, 90m, IsComplete: true, Fee: 0.18m));
        run.Handle(new OrderFilled(Start.AddHours(2), "o-4", 0.4m, isLong ? 101m : 99m, IsComplete: false, Fee: 0.04m));

        var restored = StrategyRun.Restore(definition, CompositeCatalog.Empty, NoValues, conditions, run.Snapshot());

        restored.State.ShouldBe(run.State with { Closed = null });
        restored.Snapshot().ShouldBeEquivalentTo(run.Snapshot());
        IReadOnlyList<OrderCommand> next = run.Handle(new OrderFilled(Start.AddHours(3), "o-4", 0.6m, 101m, IsComplete: true));
        restored.Handle(new OrderFilled(Start.AddHours(3), "o-4", 0.6m, 101m, IsComplete: true)).ShouldBe(next);
        restored.State.ShouldBe(run.State);
    }

    [Test]
    public void Changed_balance_counts_in_the_restored_money()
    {
        StrategyDefinition definition = StrategyDefinitions.Long(EnterAndHoldExit());
        RunSnapshot snapshot = Position(2, quantity: 1m, invested: 100m) with { PastPnl = 5m };

        var restored = StrategyRun.Restore(definition, CompositeCatalog.Empty, NoValues, Conditions.Reference with { Balance = 1500m }, snapshot);

        restored.State.Cash.ShouldBe(1405m);
    }

    /// <summary>Enters 1 at the market when flat and holds the exit at <paramref name="target" /> times the average price on the whole position.</summary>
    private static Graph EnterAndHoldExit(OrderSide entry = OrderSide.Buy, OrderSide exit = OrderSide.Sell, decimal target = 1.01m)
    {
        return new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, new NodeSettings(Side: entry, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Node("target", SimpleNodeKind.Multiply, ("b", Values.Number(target)))
            .Node("exit", SimpleNodeKind.HoldLimit, new NodeSettings(Side: exit, SizeMode: OrderSizeMode.Quantity), ("when", Values.Boolean(true)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Wire("position.averagePrice", "target.a")
            .Wire("target.result", "exit.price")
            .Wire("position.quantity", "exit.size")
            .Build();
    }

    private static RunSnapshot Position(int number, decimal quantity, decimal invested)
    {
        return new RunSnapshot(
            number,
            quantity,
            invested,
            Collateral: invested,
            PastPnl: 0m,
            RealizedPnl: 0m,
            Fees: 0m,
            LastEntryPrice: null,
            LastEntryAt: null,
            Closing: false,
            SkippedOrders: 0,
            Orders: [],
            LastOrderNumber: 0);
    }

    private static StrategyRun Restore(StrategyDefinition definition, RunSnapshot snapshot)
    {
        return StrategyRun.Restore(definition, CompositeCatalog.Empty, NoValues, Conditions.Neutral, snapshot);
    }

    private static IReadOnlyList<OrderCommand> Close(StrategyRun run, decimal close)
    {
        return run.Handle(new BarClosed(Start, new Bar(close, close, close, close, 1m)));
    }
}