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

/// <summary>Requirements "Последовательность событий", "Путь цены внутри свечи", "Новый ордер работает со следующей свечи", "Подтверждение отмены".</summary>
public class ReplaySequenceTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T10:00:00Z");

    // Scenario "Исполнение раньше закрытия".
    [Test]
    public void Fill_on_a_candle_carries_its_open_time_and_comes_before_its_close()
    {
        IReplayObserver observer = Replay(HoldBuyAt(99m), Candle(0, 100m, 101m, 98m, 100m));

        Received.InOrder(() =>
        {
            observer.Filled(Arg.Is<ReplayFill>(fill => fill!.At == Start && fill.Price == 99m && fill.Side == OrderSide.Buy));
            observer.BarClosed(Start.AddHours(1), Arg.Any<Candle>());
        });
    }

    // Scenario "Покупка раньше продажи".
    [Test]
    public void Buy_on_the_way_down_fills_before_the_sell_on_the_way_up()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Node("buy", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("price", Values.Number(99m)), ("size", Values.Number(1m)))
            .Node("sell", SimpleNodeKind.HoldLimit, Settings(OrderSide.Sell, OrderSizeMode.Quantity), ("price", Values.Number(101m)), ("size", Values.Number(1m)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Wire("position.hasPosition", "sell.when")
            .Build();

        IReplayObserver observer = Replay(StrategyDefinitions.Long(graph), Candle(0, 100m, 100m, 100m, 100m), Candle(1, 100m, 102m, 98m, 100m));

        Fills(observer).Select(fill => (fill.Side, fill.Price, fill.At)).ShouldBe(
        [
            (OrderSide.Buy, 100m, Start),
            (OrderSide.Buy, 99m, Start.AddHours(1)),
            (OrderSide.Sell, 101m, Start.AddHours(1))
        ]);
    }

    // Scenario "Ответ на исполнение внутри свечи".
    [Test]
    public void Order_placed_in_answer_to_a_fill_works_from_the_next_candle()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("buy", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("price", Values.Number(99m)), ("size", Values.Number(1m)))
            .Node("sell", SimpleNodeKind.HoldLimit, Settings(OrderSide.Sell, OrderSizeMode.Quantity), ("price", Values.Number(100m)), ("size", Values.Number(1m)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "buy.when")
            .Wire("position.hasPosition", "sell.when")
            .Build();

        IReplayObserver observer = Replay(StrategyDefinitions.Long(graph), Candle(0, 100m, 102m, 98m, 100m), Candle(1, 100.5m, 101m, 100.5m, 101m));

        Fills(observer).Select(fill => (fill.Side, fill.Price, fill.At)).ShouldBe([(OrderSide.Buy, 99m, Start), (OrderSide.Sell, 100m, Start.AddHours(1))]);
    }

    // Scenario "Отмена после исполнения другого ордера".
    [Test]
    public void Sell_that_ladder_cancels_after_its_buy_filled_does_not_fill_on_the_same_candle()
    {
        StrategyDefinition ladder = StrategyJson.ParseStrategy(StrategyFixtures.Element("ladder"));

        IReplayObserver observer = Replay(ladder, Candle(0, 100m, 100m, 100m, 100m), Candle(1, 100m, 102m, 98.5m, 100m));

        Fills(observer).Select(fill => (fill.Side, fill.Price, fill.At)).ShouldBe([(OrderSide.Buy, 100m, Start), (OrderSide.Buy, 99m, Start.AddHours(1))]);
    }

    private static StrategyDefinition HoldBuyAt(decimal price)
    {
        return StrategyDefinitions.Long(new GraphBuilder()
            .Node("buy", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("price", Values.Number(price)), ("size", Values.Number(1m)))
            .Build());
    }

    private static IReplayObserver Replay(StrategyDefinition definition, params Candle[] candles)
    {
        IReplayObserver observer = Substitute.For<IReplayObserver>();
        var replay = new BacktestReplay(
            StrategySnapshot.Of(definition, BuiltInComposites.Catalog),
            new Dictionary<string, NodeValue>(),
            Conditions.Reference,
            0m,
            LimitFillMode.Breakout,
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