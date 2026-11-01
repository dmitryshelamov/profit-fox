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
        result.Fills.Select(fill => (fill.Side, fill.Price, fill.At)).ShouldBe(
        [
            (OrderSide.Buy, 100m, Start),
            (OrderSide.Buy, 99m, Start.AddHours(1)),
            (OrderSide.Buy, 98.01m, Start.AddHours(2)),
            (OrderSide.Sell, 100.02m, Start.AddHours(3)),
            (OrderSide.Buy, 100m, Start.AddHours(4))
        ]);
        BacktestTrade closed = result.Trades[0];
        // The coin fees leave 3.029044 in the position: the sell of 3.029 at 100.02 closes it and leaves dust.
        (closed.OpenedAt, closed.ClosedAt, closed.ExitPrice).ShouldBe((Start, Start.AddHours(3), 100.02m));
        closed.EntryPrice.ShouldBe((100m * 1m + 99m * 1.010m + 98.01m * 1.020m) / 3.030m);
        // Cash 1002.939787884 after the sell, from 1000, and the dust of 0.000044 at the price of the sell.
        closed.Pnl.ShouldBe(2.939787884m + 0.000044m * 100.02m);
        closed.Fees.ShouldBe(result.Fills.Take(4).Sum(fill => fill.FeeAsset == FeeAsset.Base ? fill.Fee * fill.Price : fill.Fee));
        BacktestTrade open = result.Trades[1];
        (open.OpenedAt, open.ClosedAt, open.ExitPrice, open.Pnl).ShouldBe((Start.AddHours(4), null, null, null));
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

    [Test]
    public void Details_without_candles_have_no_fills_trades_or_curve()
    {
        var details = new DetailsObserver(1000m);

        Replay(StrategyJson.ParseStrategy(StrategyFixtures.Element("ladder")), Conditions.Reference).Run(Start, [], details);

        (details.Details().Fills.Count, details.Details().Trades.Count, details.Details().Equity.Count).ShouldBe((0, 0, 0));
    }

    [Test]
    public void Trade_closed_before_the_end_is_the_only_trade()
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

        BacktestTrade trade = details.Details().Trades.ShouldHaveSingleItem();
        (trade.OpenedAt, trade.ClosedAt, trade.EntryPrice, trade.ExitPrice, trade.Pnl).ShouldBe((Start.AddHours(6), Start.AddHours(9), 12m, 7m, -41.665m));
    }

    private static BacktestReplay Replay(StrategyDefinition definition, TradingConditions conditions)
    {
        return new BacktestReplay(StrategySnapshot.Of(definition, BuiltInComposites.Catalog), new Dictionary<string, NodeValue>(), conditions, 0m, LimitFillMode.Breakout, Interval.OneHour);
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