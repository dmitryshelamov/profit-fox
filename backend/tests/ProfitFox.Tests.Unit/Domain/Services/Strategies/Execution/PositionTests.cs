using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

public class PositionTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T10:00:00Z");

    [Test]
    public void Two_entries_and_an_exit_of_a_long()
    {
        StrategyRun run = EnterTwiceThenExit(StrategyDirection.Long);
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true));

        IReadOnlyList<OrderCommand> commands = run.Handle(new OrderFilled(Start, "o-2", 1m, 80m, IsComplete: true));

        Position(run).ShouldBe((true, 2m, 90m, 180m, 80m));
        commands.ShouldBe([new PlaceOrder("o-3", "exit", OrderSide.Sell, OrderType.Market, null, 2m, null)]);

        run.Handle(new OrderFilled(Start, "o-3", 2m, 95m, IsComplete: true));

        Position(run).ShouldBe((false, 0m, null, 0m, null));
    }

    [Test]
    public void Two_entries_and_an_exit_of_a_short()
    {
        StrategyRun run = EnterTwiceThenExit(StrategyDirection.Short);
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true));

        IReadOnlyList<OrderCommand> commands = run.Handle(new OrderFilled(Start, "o-2", 1m, 120m, IsComplete: true));

        Position(run).ShouldBe((true, 2m, 110m, 220m, 120m));
        commands.ShouldBe([new PlaceOrder("o-3", "exit", OrderSide.Buy, OrderType.Market, null, 2m, null)]);

        run.Handle(new OrderFilled(Start, "o-3", 2m, 90m, IsComplete: true));

        Position(run).ShouldBe((false, 0m, null, 0m, null));
    }

    [Test]
    public void Partial_exit_keeps_the_average_entry_price()
    {
        StrategyRun run = EnterTwiceThenExit(StrategyDirection.Long);
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true));
        run.Handle(new OrderFilled(Start, "o-2", 1m, 80m, IsComplete: true));

        run.Handle(new OrderFilled(Start, "o-3", 0.5m, 95m, IsComplete: false));

        Position(run).ShouldBe((true, 1.5m, 90m, 135m, 80m));
    }

    [Test]
    public void Time_since_the_last_entry_is_measured_to_the_event()
    {
        StrategyRun run = EnterTwiceThenExit(StrategyDirection.Long);
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true));

        run.Handle(new BarClosed(Start.AddHours(1), new Bar(1m, 1m, 1m, 1m, 1m)));

        run.ValueOf("position", "sinceLastEntry").ShouldBe(Values.Duration(TimeSpan.FromHours(1)));
    }

    [Test]
    public void Without_a_position_the_entry_values_are_missing()
    {
        StrategyRun run = EnterTwiceThenExit(StrategyDirection.Long);

        run.Handle(new BarClosed(Start, new Bar(1m, 1m, 1m, 1m, 1m)));

        run.ValueOf("position", "sinceLastEntry").ShouldBeNull();
        Position(run).ShouldBe((false, 0m, null, 0m, null));
    }

    /// <summary>Enters one unit at a time until the position holds two units, then exits both.</summary>
    private static StrategyRun EnterTwiceThenExit(StrategyDirection direction)
    {
        OrderSide entry = direction == StrategyDirection.Long ? OrderSide.Buy : OrderSide.Sell;
        OrderSide exit = direction == StrategyDirection.Long ? OrderSide.Sell : OrderSide.Buy;
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("full", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.GreaterOrEqual), ("b", Values.Number(2m)))
            .Node("notFull", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, new NodeSettings(Side: entry, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Node("exit", SimpleNodeKind.MarketOrder, new NodeSettings(Side: exit, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(2m)))
            .Wire("position.quantity", "full.a")
            .Wire("full.result", "notFull.value")
            .Wire("notFull.result", "enter.when")
            .Wire("full.result", "exit.when")
            .Build();

        return StrategyRun.Start(new StrategyDefinition(direction, [], graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral);
    }

    private static (bool Has, decimal Quantity, decimal? Average, decimal Invested, decimal? LastEntry) Position(StrategyRun run)
    {
        return (
            ((BooleanValue)run.ValueOf("position", "hasPosition")!).Value,
            ((NumberValue)run.ValueOf("position", "quantity")!).Value,
            (run.ValueOf("position", "averagePrice") as NumberValue)?.Value,
            ((NumberValue)run.ValueOf("position", "invested")!).Value,
            (run.ValueOf("position", "lastEntryPrice") as NumberValue)?.Value);
    }
}