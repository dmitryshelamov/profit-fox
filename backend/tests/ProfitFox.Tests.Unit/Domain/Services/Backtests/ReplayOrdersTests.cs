using NSubstitute;

using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>Requirements "Рыночный ордер" and "Лимитный ордер".</summary>
public class ReplayOrdersTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    // Scenario "Сигнал на закрытии бара".
    [Test]
    public void Market_buy_by_amount_from_a_close_fills_at_the_next_open_for_the_amount()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("seen", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.Greater), ("b", Values.Number(0m)))
            .Node("go", SimpleNodeKind.And)
            .Node("buy", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Amount), ("size", Values.Number(100m)))
            .Wire("candle.close", "seen.a")
            .Wire("position.hasPosition", "flat.value")
            .Wire("seen.result", "go.a")
            .Wire("flat.result", "go.b")
            .Wire("go.result", "buy.when")
            .Build();

        IReadOnlyList<ReplayFill> fills = Fills(Replay(graph, 0m, LimitFillMode.Breakout, Candle(0, 60m, 60m, 60m, 60m), Candle(1, 50m, 55m, 45m, 52m)));

        fills.ShouldHaveSingleItem().ShouldBe(new ReplayFill(Start.AddHours(1), "o-1", OrderSide.Buy, OrderType.Market, 50m, 2m, 0m, FeeAsset.Base));
    }

    // Scenario "Проскальзывание".
    [Test]
    public void Slippage_raises_the_price_of_a_market_buy_and_lowers_that_of_a_market_sell()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("buy", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Node("sell", SimpleNodeKind.ClosePosition)
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "buy.when")
            .Wire("position.hasPosition", "sell.when")
            .Build();

        IReadOnlyList<ReplayFill> fills = Fills(Replay(graph, 0.001m, LimitFillMode.Breakout, Candle(0, 100m, 100m, 100m, 100m), Candle(1, 200m, 200m, 200m, 200m)));

        fills.Select(fill => (fill.Side, fill.Price)).ShouldBe([(OrderSide.Buy, 100.1m), (OrderSide.Sell, 199.8m)]);
    }

    // Scenario "Сигнал на последней свече".
    [Test]
    public void Market_order_from_the_last_close_stays_unfilled()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("seen", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.Greater), ("b", Values.Number(0m)))
            .Node("buy", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Amount), ("size", Values.Number(100m)))
            .Wire("candle.close", "seen.a")
            .Wire("seen.result", "buy.when")
            .Build();

        Fills(Replay(graph, 0m, LimitFillMode.Breakout, Candle(0, 60m, 60m, 60m, 60m))).ShouldBeEmpty();
    }

    [Test]
    public void Exit_beyond_the_position_fills_only_what_is_left_and_one_with_nothing_left_is_cancelled()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("buy", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Node("first", SimpleNodeKind.ClosePosition)
            .Node("second", SimpleNodeKind.ClosePosition)
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "buy.when")
            .Wire("position.hasPosition", "first.when")
            .Wire("position.hasPosition", "second.when")
            .Build();
        TradingConditions conditions = Conditions.Reference with { Fees = new FeeSchedule(0.001m, 0.001m, MarketCategory.Linear) };
        var details = new DetailsObserver(conditions.Balance);

        new BacktestReplay(StrategySnapshot.Of(StrategyDefinitions.Long(graph), CompositeCatalog.Empty), new Dictionary<string, NodeValue>(), conditions, 0m, LimitFillMode.Breakout, Interval.OneHour)
            .Run(Start, [Candle(0, 100m, 100m, 100m, 100m), Candle(1, 100m, 100m, 100m, 100m), Candle(2, 100m, 100m, 100m, 100m)], details);

        BacktestDetails result = details.Details();
        result.Fills.Select(fill => (fill.Side, fill.Quantity)).ShouldBe([(OrderSide.Buy, 1m), (OrderSide.Sell, 1m), (OrderSide.Buy, 1m)]);
        (result.Metrics.Fees, result.Metrics.EndEquity).ShouldBe((0.3m, 999.7m));
    }

    [TestCase(100000, 0.001)]
    [TestCase(10000, 0.01)]
    public void Market_buy_by_amount_below_the_minimum_quantity_at_the_fill_price_is_cancelled(decimal price, decimal minQuantity)
    {
        StrategyDefinition buy = StrategyDefinitions.Long(new GraphBuilder()
            .Node("buy", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Amount), ("when", Values.Boolean(true)), ("size", Values.Number(50m)))
            .Build());
        IReplayObserver observer = Substitute.For<IReplayObserver>();
        TradingConditions conditions = Conditions.Reference with { Rules = new InstrumentRules(0.01m, 0.001m, minQuantity, 5m) };

        new BacktestReplay(StrategySnapshot.Of(buy, CompositeCatalog.Empty), new Dictionary<string, NodeValue>(), conditions, 0m, LimitFillMode.Breakout, Interval.OneHour)
            .Run(Start, [Candle(0, price, price, price, price), Candle(1, price, price, price, price)], observer);

        Fills(observer).ShouldBeEmpty();
    }

    // Scenarios "Касание" and "Пробой".
    [TestCase(LimitFillMode.Touch, 1)]
    [TestCase(LimitFillMode.Breakout, 0)]
    public void Limit_buy_at_the_low_fills_only_on_a_touch(LimitFillMode mode, int fills)
    {
        Fills(Replay(HoldBuyAt(100m), 0m, mode, Candle(0, 102m, 103m, 100m, 101m))).Select(fill => fill.Price).ShouldBe(Enumerable.Repeat(100m, fills));
    }

    [TestCase(LimitFillMode.Touch, 1)]
    [TestCase(LimitFillMode.Breakout, 0)]
    public void Limit_sell_at_the_high_fills_only_on_a_touch(LimitFillMode mode, int fills)
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("buy", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Node("sell", SimpleNodeKind.HoldLimit, Settings(OrderSide.Sell, OrderSizeMode.Quantity), ("price", Values.Number(105m)), ("size", Values.Number(1m)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "buy.when")
            .Wire("position.hasPosition", "sell.when")
            .Build();

        IReadOnlyList<ReplayFill> all = Fills(Replay(graph, 0m, mode, Candle(0, 100m, 100m, 100m, 100m), Candle(1, 101m, 105m, 100m, 104m)));

        all.Where(fill => fill.Side == OrderSide.Sell).Select(fill => fill.Price).ShouldBe(Enumerable.Repeat(105m, fills));
    }

    // Scenario "Ордер уже исполним на открытии".
    [TestCase(LimitFillMode.Touch)]
    [TestCase(LimitFillMode.Breakout)]
    public void Limit_buy_above_the_open_fills_at_its_own_price(LimitFillMode mode)
    {
        Fills(Replay(HoldBuyAt(105m), 0m, mode, Candle(0, 100m, 101m, 99m, 100m))).ShouldHaveSingleItem().Price.ShouldBe(105m);
    }

    [Test]
    public void Limit_buy_at_the_open_fills_at_the_open_on_a_touch()
    {
        Fills(Replay(HoldBuyAt(100m), 0m, LimitFillMode.Touch, Candle(0, 100m, 101m, 100m, 100m))).ShouldHaveSingleItem().Price.ShouldBe(100m);
    }

    private static StrategyDefinition HoldBuyAt(decimal price)
    {
        return StrategyDefinitions.Long(new GraphBuilder()
            .Node("buy", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("price", Values.Number(price)), ("size", Values.Number(1m)))
            .Build());
    }

    private static IReplayObserver Replay(Graph graph, decimal slippage, LimitFillMode mode, params Candle[] candles)
    {
        return Replay(StrategyDefinitions.Long(graph), slippage, mode, candles);
    }

    private static IReplayObserver Replay(StrategyDefinition definition, decimal slippage, LimitFillMode mode, params Candle[] candles)
    {
        IReplayObserver observer = Substitute.For<IReplayObserver>();
        var replay = new BacktestReplay(
            StrategySnapshot.Of(definition, CompositeCatalog.Empty),
            new Dictionary<string, NodeValue>(),
            Conditions.Reference,
            slippage,
            mode,
            Interval.OneHour);

        replay.Run(Start, candles, observer);

        return observer;
    }

    private static IReadOnlyList<ReplayFill> Fills(IReplayObserver observer)
    {
        return [.. observer.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IReplayObserver.Filled)).Select(call => (ReplayFill)call.GetArguments()[0]!)];
    }

    private static Candle Candle(int hour, decimal open, decimal high, decimal low, decimal close)
    {
        return new Candle(Start.AddHours(hour), new Price(open), new Price(high), new Price(low), new Price(close), new Volume(1m), new Volume(1m));
    }

    private static NodeSettings Settings(OrderSide side, OrderSizeMode size)
    {
        return new NodeSettings(Side: side, SizeMode: size);
    }
}