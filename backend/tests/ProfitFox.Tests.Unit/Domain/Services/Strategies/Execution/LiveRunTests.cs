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

    [Test]
    public void Warm_up_gives_sma_rsi_std_dev_and_previous_the_values_of_a_continuous_run()
    {
        StrategyRun warmed = Indicators();
        StrategyRun continuous = Indicators();
        BarClosed[] bars = [.. Enumerable.Range(0, 30).Select(index => IndicatorBar(index))];

        foreach (BarClosed bar in bars.Take(20))
        {
            warmed.WarmUp(bar);
            continuous.Handle(bar);
        }

        foreach (BarClosed bar in bars.Skip(20))
        {
            warmed.Handle(bar);
            continuous.Handle(bar);

            foreach (string node in new[] { "sma", "rsi", "dev", "previous" })
            {
                warmed.ValueOf(node, "value").ShouldNotBeNull();
                warmed.ValueOf(node, "value").ShouldBe(continuous.ValueOf(node, "value"));
            }
        }
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

    // Scenario "Отменили выход позиции": the exit belongs to the position, so it is sold again in any status of the bot.
    [TestCase(false)]
    [TestCase(true)]
    public void Cancelled_exit_of_a_closing_position_is_sold_again(bool strategyRuns)
    {
        StrategyRun run = LadderClosingAfterExitSold(0.4m, Conditions.Neutral);
        var cancelled = new OrderCancelledByVenue(Start, "o-4");

        IReadOnlyList<OrderCommand> commands = strategyRuns ? run.Handle(cancelled) : run.Record(cancelled);

        commands.ShouldBe([new PlaceOrder("o-5", null, OrderSide.Sell, OrderType.Market, null, 0.6m, null)]);
        run.State.Status.ShouldBe(PositionStatus.Closing);
        run.Position.ShouldBe(1);
    }

    /// <summary>
    ///     The ladder with 1 bought and its limits cancelled, then closed on request: the position sold <paramref name="sold" /> of it
    ///     at the market with order o-4, and the exchange has not cancelled the rest yet.
    /// </summary>
    private static StrategyRun LadderClosingAfterExitSold(decimal sold, TradingConditions conditions)
    {
        Graph graph = LadderGraph();
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, NoValues, conditions);
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true));
        run.Close(Start).ShouldBe([new CancelOrder("o-2"), new CancelOrder("o-3"), new PlaceOrder("o-4", null, OrderSide.Sell, OrderType.Market, null, 1m, null)], ignoreOrder: true);
        run.Record(new OrderCancelled(Start, "o-2"));
        run.Record(new OrderCancelled(Start, "o-3"));
        run.Record(new OrderFilled(Start, "o-4", sold, 101m, IsComplete: false));

        return run;
    }

    // A rest below the minimum of the exchange cannot be sold: the position closes and the rest goes on to the next one.
    [Test]
    public void Cancelled_exit_leaving_less_than_the_minimum_closes_the_position_and_carries_the_rest()
    {
        StrategyRun run = LadderClosingAfterExitSold(0.9995m, Conditions.Reference);

        run.Record(new OrderCancelledByVenue(Start, "o-4")).ShouldBeEmpty();

        run.State.Closed!.Number.ShouldBe(1);
        run.Position.ShouldBe(2);
        run.State.Quantity.ShouldBe(0.0005m);
    }

    // Scenario "Остановка, пока позиция закрывается": a cancel the bot asked for does not bring the exit back.
    [Test]
    public void Exit_cancelled_at_the_request_of_the_bot_is_not_sold_again()
    {
        StrategyRun run = LadderClosingAfterExitSold(0.4m, Conditions.Neutral);

        run.Record(new OrderCancelled(Start, "o-4")).ShouldBeEmpty();

        run.State.Closed!.Number.ShouldBe(1);
        run.State.Quantity.ShouldBe(0.6m);
    }

    // Scenario "Постоянный отказ биржи": a refused exit is not sold again; the position closes and carries what it holds.
    [Test]
    public void Refused_exit_of_a_closing_position_is_not_sold_again()
    {
        StrategyRun run = LadderClosingAfterExitSold(0.4m, Conditions.Neutral);

        run.Record(new OrderRejected(Start, "o-4")).ShouldBeEmpty();

        run.State.Closed!.Number.ShouldBe(1);
        run.State.Quantity.ShouldBe(0.6m);
    }

    // The exchange may confirm a cancel the run has already seen.
    [Test]
    public void Cancel_of_an_order_the_run_does_not_know_changes_nothing()
    {
        StrategyRun run = LadderClosingAfterExitSold(0.4m, Conditions.Neutral);

        run.Record(new OrderCancelledByVenue(Start, "o-99")).ShouldBeEmpty();

        run.State.Status.ShouldBe(PositionStatus.Closing);
        run.State.Quantity.ShouldBe(0.6m);
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
        return StrategyRun.Start(StrategyDefinitions.Long(LadderGraph()), CompositeCatalog.Empty, NoValues, Conditions.Neutral);
    }

    private static Graph LadderGraph()
    {
        return new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("next", SimpleNodeKind.Subtract, ("a", Values.Number(100m)))
            .Node("buy", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("size", Values.Number(1m)))
            .Node("sell", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Sell, SizeMode: OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("price", Values.Number(101m)))
            .Wire("position.quantity", "next.b")
            .Wire("next.result", "buy.price")
            .Wire("position.quantity", "sell.size")
            .Build();
    }

    private static StrategyRun Indicators()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("sma", SimpleNodeKind.Sma, ("period", Values.Number(5m)))
            .Node("rsi", SimpleNodeKind.Rsi, ("period", Values.Number(5m)))
            .Node("dev", SimpleNodeKind.StdDev, ("period", Values.Number(5m)))
            .Node("previous", SimpleNodeKind.Previous)
            .Wire("candle.close", "sma.source")
            .Wire("candle.close", "rsi.source")
            .Wire("candle.close", "dev.source")
            .Wire("candle.close", "previous.value")
            .Build();

        return StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, NoValues, Conditions.Neutral);
    }

    private static BarClosed IndicatorBar(int index)
    {
        decimal close = 100m + index * 7 % 11 - 5m;

        return new BarClosed(Start.AddHours(index), new Bar(close, close, close, close, 1m));
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