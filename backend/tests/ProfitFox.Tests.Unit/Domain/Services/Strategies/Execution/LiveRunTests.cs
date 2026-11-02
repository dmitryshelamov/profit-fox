using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

/// <summary>What a live bot needs beyond a backtest: warming the indicators up and keeping the books while the strategy is paused.</summary>
public class LiveRunTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly IReadOnlyDictionary<string, NodeValue> NoValues = new Dictionary<string, NodeValue>();
    private static readonly decimal[] Closes = [100m, 104m, 98m, 103m, 107m, 101m];

    // Scenario "EMA после запуска".
    [Test]
    public void Warm_up_gives_the_indicators_their_history_without_orders()
    {
        StrategyRun warmed = BuyAboveEma();
        StrategyRun continuous = BuyAboveEma();

        for (int index = 0; index < Closes.Length - 1; index++)
        {
            warmed.WarmUp(Bar(index));
            continuous.Handle(Bar(index));
        }

        warmed.State.ShouldBe(BuyAboveEma().State);
        warmed.Handle(Bar(Closes.Length - 1));
        continuous.Handle(Bar(Closes.Length - 1));
        warmed.ValueOf("ema", "value").ShouldBe(continuous.ValueOf("ema", "value"));
    }

    // Scenario "Исполнение на паузе".
    [Test]
    public void Recorded_fill_changes_the_position_and_places_nothing()
    {
        StrategyRun run = Ladder();
        run.Handle(new RunStarted(Start)).ShouldBe([Limit("o-1", "buy", OrderSide.Buy, 100m)]);

        run.Record(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true)).ShouldBeEmpty();

        run.State.Quantity.ShouldBe(1m);
    }

    // Scenario "Выход на паузе": the rules of the position work without the strategy.
    [Test]
    public void Recorded_exit_cancels_what_the_position_left_and_starts_the_next_one()
    {
        StrategyRun run = Ladder();
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true))
            .ShouldBe([Limit("o-2", "buy", OrderSide.Buy, 99m), Limit("o-3", "sell", OrderSide.Sell, 101m)]);

        run.Record(new OrderFilled(Start, "o-3", 1m, 101m, IsComplete: true)).ShouldBe([new CancelOrder("o-2")]);
        run.Record(new OrderCancelled(Start, "o-2")).ShouldBeEmpty();
        run.Position.ShouldBe(2);
    }

    /// <summary>Buys 1 at the market when the close is above its EMA of 3.</summary>
    private static StrategyRun BuyAboveEma()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("ema", SimpleNodeKind.Ema, ("period", Values.Number(3m)))
            .Node("above", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.Greater))
            .Node("buy", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Wire("candle.close", "ema.source")
            .Wire("candle.close", "above.a")
            .Wire("ema.value", "above.b")
            .Wire("above.result", "buy.when")
            .Build();

        return StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, NoValues, Conditions.Neutral);
    }

    /// <summary>Holds a buy of 1 at 100 less 1 per unit held and a sell of the position at 101.</summary>
    private static StrategyRun Ladder()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("next", SimpleNodeKind.Subtract, ("a", Values.Number(100m)))
            .Node("buy", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("size", Values.Number(1m)))
            .Node("sell", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Sell, SizeMode: OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("price", Values.Number(101m)))
            .Wire("position.quantity", "next.b")
            .Wire("next.result", "buy.price")
            .Wire("position.quantity", "sell.size")
            .Build();

        return StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, NoValues, Conditions.Neutral);
    }

    private static BarClosed Bar(int index)
    {
        decimal close = Closes[index];

        return new BarClosed(Start.AddHours(index), new Bar(close, close, close, close, 1m));
    }

    private static PlaceOrder Limit(string id, string node, OrderSide side, decimal price)
    {
        return new PlaceOrder(id, node, side, OrderType.Limit, price, 1m, null);
    }
}