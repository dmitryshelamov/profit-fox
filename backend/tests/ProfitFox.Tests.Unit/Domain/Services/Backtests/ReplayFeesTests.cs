using NSubstitute;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>Requirements "Комиссии" and "Конец периода".</summary>
public class ReplayFeesTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    // Scenario "Покупка на споте".
    [Test]
    public void Spot_market_buy_pays_the_market_rate_in_the_coin_and_the_sell_in_the_quote_asset()
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

        IReadOnlyList<ReplayFill> fills = Fills(Replay(StrategyDefinitions.Long(graph), MarketCategory.Spot, Candle(0, 100m), Candle(1, 110m)));

        fills.Select(fill => (fill.Quantity, fill.Fee, fill.FeeAsset)).ShouldBe([(1m, 0.001m, FeeAsset.Base), (0.999m, 0.999m * 110m * 0.001m, FeeAsset.Quote)]);
    }

    // Scenario "Сделка на linear".
    [Test]
    public void Linear_limit_buy_pays_the_limit_rate_in_the_quote_asset()
    {
        StrategyDefinition definition = StrategyDefinitions.Long(new GraphBuilder()
            .Node("buy", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("price", Values.Number(100m)), ("size", Values.Number(2m)))
            .Build());

        ReplayFill fill = Fills(Replay(definition, MarketCategory.Linear, Candle(0, 99m))).First();

        (fill.Type, fill.Fee, fill.FeeAsset).ShouldBe((OrderType.Limit, 0.04m, FeeAsset.Quote));
    }

    // Scenario "Открытая позиция на конец".
    [Test]
    public void Run_ends_after_the_last_candle_without_selling_the_position()
    {
        StrategyDefinition dca = StrategyJson.ParseStrategy(StrategyFixtures.Element("dca"));

        IReplayObserver observer = Replay(dca, MarketCategory.Spot, Candle(0, 100m), Candle(1, 90m), Candle(2, 80m));

        Fills(observer).ShouldAllBe(fill => fill.Side == OrderSide.Buy);
        Fills(observer).Count.ShouldBe(2);
        observer.ReceivedCalls().Last().GetMethodInfo().Name.ShouldBe(nameof(IReplayObserver.BarClosed));
    }

    private static IReplayObserver Replay(StrategyDefinition definition, MarketCategory category, params Candle[] candles)
    {
        IReplayObserver observer = Substitute.For<IReplayObserver>();
        TradingConditions conditions = Conditions.Reference with { Fees = new FeeSchedule(0.0002m, 0.001m, category) };
        var replay = new BacktestReplay(StrategySnapshot.Of(definition, BuiltInComposites.Catalog), new Dictionary<string, NodeValue>(), conditions, 0m, LimitFillMode.Breakout, Interval.OneHour);

        replay.Run(Start, candles, observer);

        return observer;
    }

    private static IReadOnlyList<ReplayFill> Fills(IReplayObserver observer)
    {
        return [.. observer.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IReplayObserver.Filled)).Select(call => (ReplayFill)call.GetArguments()[0]!)];
    }

    private static Candle Candle(int hour, decimal price)
    {
        return new Candle(Start.AddHours(hour), new Price(price), new Price(price), new Price(price), new Price(price), new Volume(1m), new Volume(1m));
    }

    private static NodeSettings Settings(OrderSide side, OrderSizeMode size)
    {
        return new NodeSettings(Side: side, SizeMode: size);
    }
}