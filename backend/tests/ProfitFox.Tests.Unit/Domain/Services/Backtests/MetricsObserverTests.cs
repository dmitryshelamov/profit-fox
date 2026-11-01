using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>Metrics of the requirements of backtest-results on small reference candle sets.</summary>
public class MetricsObserverTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    // Scenario "Открытая позиция с прибылью".
    [Test]
    public void Open_position_in_profit_is_unrealized_pnl_valued_at_the_last_close()
    {
        BacktestMetrics metrics = Run(BuyOnStart(1m), 1000m, Candle(0, 100m, 100m, 100m, 110m));

        (metrics.StartEquity, metrics.EndEquity, metrics.Pnl, metrics.RealizedPnl, metrics.UnrealizedPnl, metrics.ReturnPercent)
            .ShouldBe((1000m, 1010m, 10m, 0m, 10m, 1m));
        (metrics.MaxDrawdownPercent, metrics.ReturnToDrawdown).ShouldBe((0m, null));
        (metrics.Fills, metrics.Candles, metrics.ClosedTrades, metrics.WinRatePercent).ShouldBe((1, 1, 0, null));
    }

    // Scenario "Резерв не уменьшает капитал".
    [Test]
    public void Standing_limit_buy_does_not_lower_the_equity()
    {
        StrategyDefinition holdBuy = StrategyDefinitions.Long(new GraphBuilder()
            .Node("buy", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("price", Values.Number(99m)), ("size", Values.Number(1m)))
            .Build());

        BacktestMetrics metrics = Run(holdBuy, 1000m, Flat(0, 100m));

        (metrics.EndEquity, metrics.MaxDrawdownPercent, metrics.MaxCapitalUsed).ShouldBe((1000m, 0m, 99m));
    }

    // Scenario "Просадка от пика".
    [Test]
    public void Drawdown_is_the_largest_fall_of_equity_at_closes_from_its_previous_peak()
    {
        BacktestMetrics metrics = Run(BuyOnStart(10m), 1000m, Flat(0, 100m), Flat(1, 110m), Flat(2, 99m), Flat(3, 105m));

        metrics.MaxDrawdownPercent.ShouldBe(10m);
        metrics.ReturnPercent.ShouldBe(5m);
        metrics.ReturnToDrawdown.ShouldBe(0.5m);
    }

    // Scenario "Цикл Ladder".
    [Test]
    public void Ladder_cycle_of_an_entry_two_averaging_buys_and_a_sell_is_one_closed_trade_of_four_fills()
    {
        StrategyDefinition ladder = StrategyJson.ParseStrategy(StrategyFixtures.Element("ladder"));

        BacktestMetrics metrics = Run(
            ladder,
            1000m,
            Flat(0, 100m),
            Candle(1, 100m, 100m, 98.9m, 99m),
            Candle(2, 99m, 99m, 97.9m, 98m),
            Candle(3, 98m, 101m, 98m, 100.5m));

        (metrics.Fills, metrics.ClosedTrades, metrics.WinRatePercent).ShouldBe((4, 1, 100m));
        metrics.RealizedPnl.ShouldBe(metrics.Pnl);
    }

    // Scenario "DCA без выхода".
    [Test]
    public void Dca_without_a_sell_has_no_closed_trade_and_no_win_rate()
    {
        StrategyDefinition dca = StrategyJson.ParseStrategy(StrategyFixtures.Element("dca"));

        BacktestMetrics metrics = Run(dca, 1000m, new Dictionary<string, NodeValue> { ["limitDeposit"] = Values.Number(300m) }, Flat(0, 100m), Flat(1, 90m), Flat(2, 80m));

        (metrics.Fills, metrics.ClosedTrades, metrics.WinRatePercent).ShouldBe((3, 0, null));
    }

    // Scenario "Комиссия в монете".
    [Test]
    public void Fee_in_the_coin_counts_at_the_price_of_its_fill()
    {
        TradingConditions spot = Conditions.Reference with { Fees = new FeeSchedule(0m, 0.001m, MarketCategory.Spot) };

        BacktestMetrics metrics = Run(BuyThenSell(), spot, new Dictionary<string, NodeValue>(), Flat(0, 100m), Flat(1, 110m));

        metrics.Fees.ShouldBe(0.001m * 100m + 0.999m * 110m * 0.001m);
        metrics.EndEquity.ShouldBe(1000m - 100m + 0.999m * 110m - 0.999m * 110m * 0.001m);
        (metrics.ClosedTrades, metrics.WinRatePercent).ShouldBe((1, 100m));
    }

    // Scenario "Время под водой".
    [Test]
    public void Time_in_position_and_under_water_are_shares_of_closes()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("high", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.Greater), ("b", Values.Number(150m)))
            .Node("go", SimpleNodeKind.And)
            .Node("buy", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Wire("candle.close", "high.a")
            .Wire("position.hasPosition", "flat.value")
            .Wire("high.result", "go.a")
            .Wire("flat.result", "go.b")
            .Wire("go.result", "buy.when")
            .Build();
        decimal[] closes = [100m, 100m, 100m, 160m, 150m, 140m, 155m, 159m, 170m, 165m];

        BacktestMetrics metrics = Run(StrategyDefinitions.Long(graph), 1000m, [.. closes.Select((close, hour) => Candle(hour, hour == 4 ? 160m : close, Math.Max(close, 160m), close, close))]);

        (metrics.TimeInPositionPercent, metrics.TimeUnderWaterPercent, metrics.LongestUnderWaterCandles).ShouldBe((60m, 40m, 4));
    }

    // Scenario "Не хватило баланса".
    [Test]
    public void Buys_that_the_balance_does_not_pay_for_are_skipped_orders()
    {
        StrategyDefinition dca = StrategyJson.ParseStrategy(StrategyFixtures.Element("dca"));

        BacktestMetrics metrics = Run(dca, 250m, new Dictionary<string, NodeValue> { ["limitDeposit"] = Values.Number(300m) }, Flat(0, 100m), Flat(1, 100m));

        (metrics.Fills, metrics.SkippedOrders).ShouldBe((2, 1L));
    }

    [Test]
    public void Cost_of_capital_counts_the_position_at_entry_and_the_reserve_of_standing_entries()
    {
        StrategyDefinition holdBuy = StrategyDefinitions.Long(new GraphBuilder()
            .Node("buy", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("price", Values.Number(99m)), ("size", Values.Number(1m)))
            .Build());

        BacktestMetrics metrics = Run(holdBuy, 1000m, Flat(0, 100m), Candle(1, 100m, 100m, 98m, 100m));

        (metrics.MaxPositionCost, metrics.MaxPositionCostPercent, metrics.MaxCapitalUsed).ShouldBe((99m, 9.9m, 198m));
    }

    [Test]
    public void Losing_trade_counts_against_the_win_rate()
    {
        BacktestMetrics metrics = Run(BuyThenSell(), 1000m, Flat(0, 100m), Flat(1, 90m));

        (metrics.ClosedTrades, metrics.WinRatePercent, metrics.RealizedPnl).ShouldBe((1, 0m, -10m));
    }

    [Test]
    public void Run_without_candles_keeps_the_balance()
    {
        BacktestMetrics metrics = Run(BuyOnStart(1m), 1000m);

        (metrics.EndEquity, metrics.Candles, metrics.TimeInPositionPercent, metrics.TimeUnderWaterPercent).ShouldBe((1000m, 0, 0m, 0m));
    }

    /// <summary>Buys once: the position is never sold.</summary>
    private static StrategyDefinition BuyOnStart(decimal quantity)
    {
        return StrategyDefinitions.Long(new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("buy", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(quantity)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "buy.when")
            .Build());
    }

    /// <summary>Buys one unit while flat and sells the whole position while it has one.</summary>
    private static StrategyDefinition BuyThenSell()
    {
        return StrategyDefinitions.Long(new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("buy", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Node("sell", SimpleNodeKind.ClosePosition)
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "buy.when")
            .Wire("position.hasPosition", "sell.when")
            .Build());
    }

    private static BacktestMetrics Run(StrategyDefinition definition, decimal balance, params Candle[] candles)
    {
        return Run(definition, balance, new Dictionary<string, NodeValue>(), candles);
    }

    private static BacktestMetrics Run(StrategyDefinition definition, decimal balance, IReadOnlyDictionary<string, NodeValue> values, params Candle[] candles)
    {
        return Run(definition, Conditions.Reference with { Balance = balance }, values, candles);
    }

    private static BacktestMetrics Run(StrategyDefinition definition, TradingConditions conditions, IReadOnlyDictionary<string, NodeValue> values, params Candle[] candles)
    {
        var observer = new MetricsObserver(conditions.Balance);
        new BacktestReplay(StrategySnapshot.Of(definition, BuiltInComposites.Catalog), values, conditions, 0m, LimitFillMode.Breakout, Interval.OneHour)
            .Run(Start, candles, observer);

        return observer.Metrics();
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