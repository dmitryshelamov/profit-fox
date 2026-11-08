using NSubstitute;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Infrastructure.Exchanges.Routing;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Routing;

/// <summary>Decision 2: the ports of trading go to the exchange of the key, the candles to the exchange of the category.</summary>
public class RoutedTradingPortsTests
{
    private static readonly ExchangeKey Bybit = new ExchangeKey("api-key", "api-secret");
    private static readonly ExchangeKey TInvest = new ExchangeKey("2000000001", "t.token", Exchange.TInvest);
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly Instrument Sber = new Instrument(MarketCategory.Moex, "SBER");

    [TestCase(false)]
    [TestCase(true)]
    public async Task Orders_and_cancels_go_to_the_exchange_of_the_key(bool tinvest)
    {
        IOrderGateway bybit = Substitute.For<IOrderGateway>();
        IOrderGateway tinvestGateway = Substitute.For<IOrderGateway>();
        var routed = new RoutedOrderGateway(bybit, tinvestGateway);
        (ExchangeKey key, Instrument instrument, IOrderGateway expected, IOrderGateway other) = tinvest ? (TInvest, Sber, tinvestGateway, bybit) : (Bybit, Btc, bybit, tinvestGateway);
        var order = new OrderRequest("c-1", OrderSide.Buy, OrderType.Limit, 100m, 10m, null);
        var accepted = new OrderOutcome(OrderOutcomeKind.Accepted, "e-1");
        expected.PlaceAsync(key, instrument, order, Arg.Any<CancellationToken>()).Returns(accepted);
        expected.CancelAsync(key, instrument, "c-1", Arg.Any<CancellationToken>()).Returns(accepted);
        expected.CancelByExchangeIdAsync(key, instrument, "e-1", Arg.Any<CancellationToken>()).Returns(accepted);
        expected.SetLeverageAsync(key, instrument, 5, Arg.Any<CancellationToken>()).Returns(accepted);

        (await routed.PlaceAsync(key, instrument, order, CancellationToken.None)).ShouldBe(accepted);
        (await routed.CancelAsync(key, instrument, "c-1", CancellationToken.None)).ShouldBe(accepted);
        (await routed.CancelByExchangeIdAsync(key, instrument, "e-1", CancellationToken.None)).ShouldBe(accepted);
        (await routed.SetLeverageAsync(key, instrument, 5, CancellationToken.None)).ShouldBe(accepted);

        other.ReceivedCalls().ShouldBeEmpty();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Reads_of_an_account_go_to_the_exchange_of_the_key(bool tinvest)
    {
        IExchangeLedger bybit = Substitute.For<IExchangeLedger>();
        IExchangeLedger tinvestLedger = Substitute.For<IExchangeLedger>();
        var routed = new RoutedExchangeLedger(bybit, tinvestLedger);
        (ExchangeKey key, Instrument instrument, IExchangeLedger expected, IExchangeLedger other) = tinvest ? (TInvest, Sber, tinvestLedger, bybit) : (Bybit, Btc, bybit, tinvestLedger);
        var order = new ExchangeOrder("c-1", "e-1", ExchangeOrderState.Open, 0m, null);
        var fill = new ExchangeFill("x-1", "c-1", 1m, 100m, 0m, "", DateTimeOffset.UnixEpoch);
        var fees = new FeeSchedule(0.1m, 0.2m, instrument.Category);
        expected.GetFreeAsync(key, "RUB", Arg.Any<CancellationToken>()).Returns(5m);
        expected.IsHedgeModeAsync(key, instrument, Arg.Any<CancellationToken>()).Returns(true);
        expected.GetHoldingAsync(key, instrument, "SBER", Arg.Any<CancellationToken>()).Returns(7m);
        expected.GetLeverageAsync(key, instrument, Arg.Any<CancellationToken>()).Returns(3m);
        expected.GetOpenOrdersAsync(key, instrument, Arg.Any<CancellationToken>()).Returns([order]);
        expected.FindOrderAsync(key, instrument, "c-1", Arg.Any<CancellationToken>()).Returns(order);
        expected.FindOrderByExchangeIdAsync(key, instrument, "e-1", Arg.Any<CancellationToken>()).Returns(order);
        expected.GetOrdersOfParentAsync(key, instrument, "c-1", Arg.Any<CancellationToken>()).Returns([order]);
        expected.GetOrderFillsAsync(key, instrument, "e-1", Arg.Any<CancellationToken>()).Returns([fill]);
        expected.GetFillsAsync(key, instrument, "c-1", Arg.Any<CancellationToken>()).Returns([fill]);
        expected.GetFeesAsync(key, instrument, Arg.Any<CancellationToken>()).Returns(fees);

        (await routed.GetFreeAsync(key, "RUB", CancellationToken.None)).ShouldBe(5m);
        (await routed.IsHedgeModeAsync(key, instrument, CancellationToken.None)).ShouldBeTrue();
        (await routed.GetHoldingAsync(key, instrument, "SBER", CancellationToken.None)).ShouldBe(7m);
        (await routed.GetLeverageAsync(key, instrument, CancellationToken.None)).ShouldBe(3m);
        (await routed.GetOpenOrdersAsync(key, instrument, CancellationToken.None)).ShouldBe([order]);
        (await routed.FindOrderAsync(key, instrument, "c-1", CancellationToken.None)).ShouldBe(order);
        (await routed.FindOrderByExchangeIdAsync(key, instrument, "e-1", CancellationToken.None)).ShouldBe(order);
        (await routed.GetOrdersOfParentAsync(key, instrument, "c-1", CancellationToken.None)).ShouldBe([order]);
        (await routed.GetOrderFillsAsync(key, instrument, "e-1", CancellationToken.None)).ShouldBe([fill]);
        (await routed.GetFillsAsync(key, instrument, "c-1", CancellationToken.None)).ShouldBe([fill]);
        (await routed.GetFeesAsync(key, instrument, CancellationToken.None)).ShouldBe(fees);

        other.ReceivedCalls().ShouldBeEmpty();
    }

    // The account stream goes by the key, which has no instrument; the candles by the category of the instrument.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Streams_of_an_account_go_by_the_key_and_candles_by_the_category(bool tinvest)
    {
        IExchangeStreams bybit = Substitute.For<IExchangeStreams>();
        IExchangeStreams tinvestStreams = Substitute.For<IExchangeStreams>();
        var routed = new RoutedExchangeStreams(bybit, tinvestStreams);
        (ExchangeKey key, Instrument instrument, IExchangeStreams expected, IExchangeStreams other) = tinvest ? (TInvest, Sber, tinvestStreams, bybit) : (Bybit, Btc, bybit, tinvestStreams);
        IAsyncDisposable candles = Substitute.For<IAsyncDisposable>();
        IAsyncDisposable account = Substitute.For<IAsyncDisposable>();
        expected.SubscribeCandlesAsync(instrument, Interval.OneHour, Arg.Any<Action<ProfitFox.Domain.Entities.MarketData.Candle>>(), Arg.Any<Action>(), Arg.Any<CancellationToken>()).Returns(candles);
        expected.SubscribeAccountAsync(key, Arg.Any<Action<ExchangeOrder>>(), Arg.Any<Action<ExchangeFill>>(), Arg.Any<Action>(), Arg.Any<CancellationToken>()).Returns(account);

        (await routed.SubscribeCandlesAsync(instrument, Interval.OneHour, _ => { }, () => { }, CancellationToken.None)).ShouldBeSameAs(candles);
        (await routed.SubscribeAccountAsync(key, _ => { }, _ => { }, () => { }, CancellationToken.None)).ShouldBeSameAs(account);

        other.ReceivedCalls().ShouldBeEmpty();
    }

    [TestCase(MarketCategory.Spot, false)]
    [TestCase(MarketCategory.Linear, false)]
    [TestCase(MarketCategory.Moex, true)]
    public async Task Forming_candles_come_from_the_exchange_of_the_category(MarketCategory category, bool tinvest)
    {
        IFormingCandleStreams bybit = Substitute.For<IFormingCandleStreams>();
        IFormingCandleStreams tinvestStreams = Substitute.For<IFormingCandleStreams>();
        var instrument = new Instrument(category, "X");
        IAsyncDisposable subscription = Substitute.For<IAsyncDisposable>();
        (tinvest ? tinvestStreams : bybit).SubscribeAsync(instrument, Interval.OneHour, default!, default!, default!, default!, default).ReturnsForAnyArgs(subscription);

        (await new RoutedFormingCandleStreams(bybit, tinvestStreams).SubscribeAsync(instrument, Interval.OneHour, _ => { }, () => { }, () => { }, () => { }, CancellationToken.None))
            .ShouldBeSameAs(subscription);

        (tinvest ? bybit : tinvestStreams).ReceivedCalls().ShouldBeEmpty();
    }
}