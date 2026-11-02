using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Backtests;
using ProfitFox.Tests.Shared.Builders.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>Requirements "Детали результата" and "Кривая капитала".</summary>
public class DetailsObserverTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    [Test]
    public void Details_of_ladder_give_the_same_metrics_as_the_metrics_observer_and_its_fills_and_trade()
    {
        StrategyDefinition ladder = StrategyJson.ParseStrategy(StrategyFixtures.Element("ladder"));
        Candle[] candles = [Flat(0, 100m), Candle(1, 100m, 100m, 98.9m, 99m), Candle(2, 99m, 99m, 97.9m, 98m), Candle(3, 98m, 101m, 98m, 100.5m), Flat(4, 100m)];
        TradingConditions conditions = Conditions.Reference with { Fees = new FeeSchedule(0.0002m, 0.00055m, MarketCategory.Spot) };
        var metrics = new MetricsObserver(conditions.Balance);
        var details = new DetailsObserver(conditions.Balance);

        Replay(ladder, conditions).Run(Start, candles, metrics);
        Replay(ladder, conditions).Run(Start, candles, details);

        BacktestDetails result = details.Details();
        result.Metrics.ShouldBe(metrics.Metrics());
        result.AllFills().Select(entry => (entry.Order.Side, entry.Fill.Price, entry.Fill.At)).ShouldBe(
        [
            (OrderSide.Buy, 100m, Start),
            (OrderSide.Buy, 99m, Start.AddHours(1)),
            (OrderSide.Buy, 98.01m, Start.AddHours(2)),
            (OrderSide.Sell, 100.02m, Start.AddHours(3)),
            (OrderSide.Buy, 100m, Start.AddHours(4))
        ]);
        BacktestPosition closed = result.Positions[0];
        // The coin fees leave 3.029044 in the position: the sell of 3.029 at 100.02 closes it and leaves a remainder.
        (closed.Number, closed.Status, closed.OpenedAt, closed.ClosedAt, closed.ExitPrice).ShouldBe((1, PositionStatus.Closed, Start, Start.AddHours(3), 100.02m));
        closed.EntryPrice.ShouldBe((100m * 1m + 99m * 1.010m + 98.01m * 1.020m) / 3.030m);
        // The sell of 3.029 at 100.02 over the average price of 299.9602 invested in 3.029044, less the limit fee of the sell; the
        // remainder of 0.000044 moves to the next position with its cost.
        closed.RealizedPnl.ShouldBe(3.029m * 100.02m - 299.9602m / 3.029044m * 3.029m - 100.02m * 3.029m * 0.0002m);
        closed.Fees.ShouldBe(result.AllFills().Take(4).Sum(entry => entry.Fill.FeeAsset == FeeAsset.Base ? entry.Fill.Fee * entry.Fill.Price : entry.Fill.Fee));
        BacktestPosition open = result.Positions[1];
        (open.Number, open.Status, open.OpenedAt, open.ClosedAt, open.ExitPrice, open.CarriedQuantity, open.EntryPrice).ShouldBe(
            (2, PositionStatus.Open, Start.AddHours(4), null, null, 0.000044m, 100m));
        result.Equity.Select(point => point.At).ShouldBe([.. candles.Select(candle => candle.OpenTime.AddHours(1))]);
        result.Equity[^1].Equity.ShouldBe(result.Metrics.EndEquity);
        result.Equity.ShouldAllBe(point => point.Equity == point.MinEquity);
    }

    // Scenario "Длинный период".
    [Test]
    public void Equity_curve_of_a_year_of_minutes_has_at_most_5000_points_that_keep_the_lowest_equity()
    {
        const int closes = 525600;
        var candles = new Candle[closes];

        for (int index = 0; index < closes; index++)
        {
            decimal price = index == 300_001 ? 37.5m : 100m + index % 1000 / 10m;
            candles[index] = new Candle(Start.AddMinutes(index), new Price(price), new Price(price), new Price(price), new Price(price), new Volume(1m), new Volume(1m));
        }

        var details = new DetailsObserver(1000m);
        StrategyDefinition buyOnce = StrategyDefinitions.Long(new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("buy", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(5m)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "buy.when")
            .Build());

        new BacktestReplay(StrategySnapshot.Of(buyOnce, BuiltInComposites.Catalog), new Dictionary<string, NodeValue>(), Conditions.Reference, 0m, LimitFillMode.Breakout, Interval.OneMinute)
            .Run(Start, candles, details);

        IReadOnlyList<EquityPoint> curve = details.Details().Equity;
        curve.Count.ShouldBeLessThanOrEqualTo(5000);
        curve.Min(point => point.MinEquity).ShouldBe(500m + 5m * 37.5m);
        curve[^1].At.ShouldBe(Start.AddMinutes(closes));
        curve[^1].Equity.ShouldBe(details.Details().Metrics.EndEquity);
    }

    // Scenario "Цены группы".
    [Test]
    public void Equity_point_of_a_group_carries_the_open_time_open_highest_high_lowest_low_and_last_close()
    {
        var candles = new List<Candle> { Candle(0, 100m, 104m, 99m, 103m), Candle(1, 103m, 106m, 101m, 102m) };
        candles.AddRange(Enumerable.Range(2, 8000).Select(hour => Flat(hour, 102m)));
        var details = new DetailsObserver(1000m);

        Replay(StrategyJson.ParseStrategy(StrategyFixtures.Element("ladder")), Conditions.Reference).Run(Start, candles, details);

        EquityPoint first = details.Details().Equity[0];
        (first.OpenTime, first.Open, first.High, first.Low, first.Close).ShouldBe((Start, 100m, 106m, 99m, 102m));
    }

    // Scenario "Одна свеча в точке".
    [Test]
    public void Equity_point_of_a_single_candle_carries_the_prices_of_that_candle()
    {
        Candle[] candles = [Candle(0, 100m, 104m, 99m, 103m), Candle(1, 103m, 106m, 101m, 102m), Candle(2, 102m, 102m, 97m, 98m)];
        var details = new DetailsObserver(1000m);

        Replay(StrategyJson.ParseStrategy(StrategyFixtures.Element("ladder")), Conditions.Reference).Run(Start, candles, details);

        details.Details().Equity.Select(point => (point.OpenTime, point.Open, point.High, point.Low, point.Close)).ShouldBe(
            [.. candles.Select(candle => (candle.OpenTime, candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value))]);
    }

    [Test]
    public void Details_without_candles_have_no_fills_or_curve()
    {
        var details = new DetailsObserver(1000m);

        Replay(StrategyJson.ParseStrategy(StrategyFixtures.Element("ladder")), Conditions.Reference).Run(Start, [], details);

        (details.Details().AllFills().Count, details.Details().Equity.Count).ShouldBe((0, 0));
    }

    [Test]
    public void Position_without_fills_and_active_orders_is_not_in_the_details()
    {
        var details = new DetailsObserver(1000m);

        Replay(StrategyDefinitions.Long(new GraphBuilder().Node("position", SimpleNodeKind.Position).Build()), Conditions.Reference).Run(Start, [Flat(0, 100m)], details);

        details.Details().Positions.ShouldBeEmpty();
    }

    // Scenario "Ордера позиции лестницы".
    [Test]
    public void Position_of_a_ladder_holds_its_filled_orders_and_not_the_ones_cancelled_at_the_close()
    {
        var details = new DetailsObserver(1000m);
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy), ("size", Values.Number(1m)))
            .Node("average1", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy), ("when", Values.Boolean(true)), ("price", Values.Number(90m)), ("size", Values.Number(1m)))
            .Node("average2", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy), ("when", Values.Boolean(true)), ("price", Values.Number(80m)), ("size", Values.Number(1m)))
            .Node("average3", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy), ("when", Values.Boolean(true)), ("price", Values.Number(70m)), ("size", Values.Number(1m)))
            .Node("take", SimpleNodeKind.HoldLimit, Settings(OrderSide.Sell), ("price", Values.Number(101m)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Wire("position.hasPosition", "take.when")
            .Wire("position.quantity", "take.size")
            .Build();

        Replay(StrategyDefinitions.Long(graph), Conditions.Reference)
            .Run(Start, [Flat(0, 100m), Candle(1, 100m, 100m, 89m, 95m), Candle(2, 95m, 102m, 95m, 100m)], details);

        BacktestPosition first = details.Details().Positions[0];
        (first.Number, first.Status, first.ClosedAt, first.RealizedPnl).ShouldBe((1, PositionStatus.Closed, Start.AddHours(2), 12m));
        first.Orders.Select(order => (order.Node, order.Status, order.Fills.Count)).ShouldBe(
            [("average1", BacktestOrderStatus.Filled, 1), ("enter", BacktestOrderStatus.Filled, 1), ("take", BacktestOrderStatus.Filled, 1)],
            ignoreOrder: true);
    }

    // Scenario "Переставленная лимитка входа".
    [Test]
    public void Re_placed_limit_entry_is_one_filled_order()
    {
        var details = new DetailsObserver(1000m);
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("candle", SimpleNodeKind.Candle)
            .Node("below", SimpleNodeKind.Multiply, ("b", Values.Number(0.99m)))
            .Node("enter", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy), ("size", Values.Number(1m)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Wire("candle.close", "below.a")
            .Wire("below.result", "enter.price")
            .Build();

        Replay(StrategyDefinitions.Long(graph), Conditions.Reference)
            .Run(Start, [Flat(0, 100m), Flat(1, 101m), Flat(2, 102m), Candle(3, 102m, 102m, 100m, 102m)], details);

        BacktestOrder order = details.Details().Positions.ShouldHaveSingleItem().Orders.ShouldHaveSingleItem();
        (order.Node, order.Status, order.Price, order.Fills.Single().Price).ShouldBe(("enter", BacktestOrderStatus.Filled, 100.98m, 100.98m));
    }

    // Scenario "Ожидающий вход на конец периода".
    [Test]
    public void Waiting_position_at_the_end_holds_its_active_order_without_fills()
    {
        var details = new DetailsObserver(1000m);
        Graph graph = new GraphBuilder()
            .Node("enter", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy), ("when", Values.Boolean(true)), ("price", Values.Number(50m)), ("size", Values.Number(1m)))
            .Build();

        Replay(StrategyDefinitions.Long(graph), Conditions.Reference).Run(Start, [Flat(0, 100m)], details);

        BacktestPosition position = details.Details().Positions.ShouldHaveSingleItem();
        (position.Number, position.Status, position.OpenedAt, position.EntryPrice).ShouldBe((1, PositionStatus.Waiting, null, null));
        BacktestOrder order = position.Orders.ShouldHaveSingleItem();
        (order.Status, order.Fills.Count).ShouldBe((BacktestOrderStatus.Active, 0));
    }

    // Scenario "Сумма по позициям".
    [Test]
    public void Realized_pnl_of_the_positions_adds_up_to_that_of_the_result()
    {
        var details = new DetailsObserver(1000m);
        TradingConditions conditions = Conditions.Reference with { Fees = new FeeSchedule(0.0002m, 0.00055m, MarketCategory.Spot) };
        Candle[] candles = [.. Enumerable.Range(0, 48).Select(hour => Flat(hour, 100m + (hour % 4 < 2 ? -2m : 2m) * (hour % 2 + 1)))];

        Replay(StrategyJson.ParseStrategy(StrategyFixtures.Element("ladder")), conditions).Run(Start, candles, details);

        BacktestDetails result = details.Details();
        result.Positions.Count(position => position.Status == PositionStatus.Closed).ShouldBeGreaterThan(0);
        result.Positions[^1].Status.ShouldNotBe(PositionStatus.Closed);
        result.Positions.Sum(position => position.RealizedPnl).ShouldBe(result.Metrics.RealizedPnl);
    }

    [Test]
    public void Position_closed_before_the_end_is_the_only_position()
    {
        StrategyDefinition ema = StrategyJson.ParseStrategy(StrategyFixtures.Element("ema"));
        decimal[] closes = [10m, 9m, 8m, 7m, 8m, 10m, 12m, 11m, 9m, 7m, 7m];
        var details = new DetailsObserver(1000m);

        new BacktestReplay(
                StrategySnapshot.Of(ema, BuiltInComposites.Catalog),
                new Dictionary<string, NodeValue> { ["fast"] = Values.Number(2m), ["slow"] = Values.Number(3m) },
                Conditions.Reference,
                0m,
                LimitFillMode.Breakout,
                Interval.OneHour)
            .Run(Start, [.. closes.Select((close, hour) => Flat(hour, close))], details);

        BacktestPosition trade = details.Details().Positions.ShouldHaveSingleItem();
        (trade.OpenedAt, trade.ClosedAt, trade.EntryPrice, trade.ExitPrice, trade.RealizedPnl).ShouldBe((Start.AddHours(6), Start.AddHours(9), 12m, 7m, -41.665m));
    }

    private static BacktestReplay Replay(StrategyDefinition definition, TradingConditions conditions)
    {
        return new BacktestReplay(StrategySnapshot.Of(definition, BuiltInComposites.Catalog), new Dictionary<string, NodeValue>(), conditions, 0m, LimitFillMode.Breakout, Interval.OneHour);
    }

    private static NodeSettings Settings(OrderSide side)
    {
        return new NodeSettings(Side: side, SizeMode: OrderSizeMode.Quantity);
    }

    private static Candle Flat(int hour, decimal price)
    {
        return Candle(hour, price, price, price, price);
    }

    private static Candle Candle(int hour, decimal open, decimal high, decimal low, decimal close)
    {
        return new Candle(Start.AddHours(hour), new Price(open), new Price(high), new Price(low), new Price(close), new Volume(1m), new Volume(1m));
    }
}