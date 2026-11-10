using System.Collections.Concurrent;
using System.Globalization;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Scanner;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges.Bybit;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>Decision 5: the minute candles of the whole market in batches, and the liquidations of linear pairs, against the streams of Bybit v5 on a local port.</summary>
public class BybitMarketStreamsTests
{
    private static readonly DateTimeOffset Open = DateTimeOffset.Parse("2024-03-10T12:04:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-08T03:10:05Z", CultureInfo.InvariantCulture);

    private FakeBybitSocketServer _server = null!;
    private BybitMarketStreams _streams = null!;

    [SetUp]
    public async Task SetUp()
    {
        _server = await FakeBybitSocketServer.StartAsync();
        _streams = new BybitMarketStreams(NullLoggerFactory.Instance, new BybitSettings(SocketAddress: _server.Address), new ScannerSettings { TopicsPerConnection = 100 }, new FakeTimeProvider(Now));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _server.DisposeAsync();
    }

    // Bybit takes at most 10 topics in a subscribe request of spot and at most 100 topics on a connection.
    [Test]
    public async Task Symbols_go_in_batches_within_the_limits_of_a_request_and_of_a_connection()
    {
        Instrument[] linear = [.. Enumerable.Range(0, 250).Select(i => new Instrument(MarketCategory.Linear, $"C{i:000}USDT"))];
        Instrument[] spot = [.. Enumerable.Range(0, 30).Select(i => new Instrument(MarketCategory.Spot, $"S{i:000}USDT"))];

        await using IAsyncDisposable subscription = await _streams.SubscribeMinuteCandlesAsync([.. linear, .. spot], (_, _) => { }, () => { }, CancellationToken.None);

        linear.ShouldAllBe(instrument => _server.Subscribed($"kline.1.{instrument.Symbol}"));
        spot.ShouldAllBe(instrument => _server.Subscribed($"kline.1.{instrument.Symbol}"));
        _server.SubscribeSizes.ShouldAllBe(size => size <= 10);
        _server.TopicsPerConnection("/v5/public/linear").ShouldAllBe(topics => topics <= 100);
        _server.Connections("/v5/public/linear").ShouldBe(3);
        _server.Connections("/v5/public/spot").ShouldBe(1);
    }

    // Requirement "Живые минутные свечи": a candle that is still forming is never stored.
    [Test]
    public async Task Only_closed_candles_come_with_their_instrument()
    {
        var candles = new BlockingCollection<(Instrument, Candle)>();
        await using IAsyncDisposable subscription = await _streams.SubscribeMinuteCandlesAsync([Sol], (instrument, candle) => candles.Add((instrument, candle)), () => { }, CancellationToken.None);

        await _server.PushAsync("kline.1.SOLUSDT", [FakeBybitSocketServer.Kline(Open, TimeSpan.FromMinutes(1), 150m, confirm: false)]);
        await _server.PushAsync("kline.1.SOLUSDT", [FakeBybitSocketServer.Kline(Open, TimeSpan.FromMinutes(1), 151.5m, confirm: true, volume: 2m)]);

        candles.TryTake(out (Instrument Instrument, Candle Candle) closed, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        closed.Instrument.ShouldBe(Sol);
        closed.Candle.ShouldBe(new Candle(Open, new Price(151.5m), new Price(151.5m), new Price(151.5m), new Price(151.5m), new Volume(2m), new Volume(151.5m)));
        candles.TryTake(out _, TimeSpan.FromMilliseconds(200)).ShouldBeFalse();
    }

    // Requirement "Пропуск после перерыва": the scanner loads what the break missed.
    [Test]
    public async Task Connection_back_after_a_break_is_told()
    {
        var restored = new SemaphoreSlim(0);
        await using IAsyncDisposable subscription = await _streams.SubscribeMinuteCandlesAsync([Sol], (_, _) => { }, () => restored.Release(), CancellationToken.None);

        await _server.DropAsync();

        (await restored.WaitAsync(TimeSpan.FromSeconds(30))).ShouldBeTrue();
    }

    [Test]
    public async Task Refused_subscription_means_the_exchange_is_unavailable()
    {
        _server.RefusedTopics["kline.1.SOLUSDT"] = true;

        await Should.ThrowAsync<ProfitFox.Application.Exceptions.ExchangeUnavailableException>(() =>
            _streams.SubscribeMinuteCandlesAsync([Sol], (_, _) => { }, () => { }, CancellationToken.None));
    }

    [Test]
    public async Task Disposing_takes_every_subscription_off()
    {
        IAsyncDisposable subscription = await _streams.SubscribeMinuteCandlesAsync([Sol, new Instrument(MarketCategory.Spot, "BTCUSDT")], (_, _) => { }, () => { }, CancellationToken.None);

        await subscription.DisposeAsync();

        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while ((_server.Connections("/v5/public/linear") + _server.Connections("/v5/public/spot")) > 0 && DateTime.UtcNow < until)
        {
            await Task.Delay(20);
        }

        _server.Subscribed("kline.1.SOLUSDT").ShouldBeFalse();
        _server.Connections("/v5/public/linear").ShouldBe(0);
        _server.Connections("/v5/public/spot").ShouldBe(0);
    }

    // Scenarios "Ликвидация пришла" and "Сторона позиции": Bybit's S is the side of the liquidated position, "a Buy update means that a
    // long position has been liquidated".
    [TestCase("Buy", LiquidationSide.Long)]
    [TestCase("Sell", LiquidationSide.Short)]
    public async Task Liquidation_comes_with_its_instrument_time_and_the_side_of_the_position(string positionSide, LiquidationSide side)
    {
        var at = DateTimeOffset.Parse("2026-11-08T12:04:07.350Z", CultureInfo.InvariantCulture);
        var liquidations = new BlockingCollection<(Instrument, LiquidationEvent)>();
        await using IAsyncDisposable subscription = await _streams.SubscribeLiquidationsAsync(
            [Sol], (instrument, liquidation) => liquidations.Add((instrument, liquidation)), _ => { }, _ => { }, CancellationToken.None);

        await _server.PushAsync("allLiquidation.SOLUSDT", [FakeBybitSocketServer.Liquidation("SOLUSDT", positionSide, 150.2m, 12.5m, at)]);

        liquidations.TryTake(out (Instrument Instrument, LiquidationEvent Event) liquidated, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        liquidated.ShouldBe((Sol, new LiquidationEvent(at, side, 150.2m, 12.5m)));
    }

    [Test]
    public async Task Liquidations_of_25_pairs_go_in_3_subscribe_requests()
    {
        Instrument[] linear = [.. Enumerable.Range(0, 25).Select(i => new Instrument(MarketCategory.Linear, $"C{i:000}USDT"))];

        await using IAsyncDisposable subscription = await _streams.SubscribeLiquidationsAsync(linear, (_, _) => { }, _ => { }, _ => { }, CancellationToken.None);

        linear.ShouldAllBe(instrument => _server.Subscribed($"allLiquidation.{instrument.Symbol}"));
        _server.SubscribeSizes.ShouldBe([10, 10, 5]);
    }

    [Test]
    public async Task Refused_liquidation_subscription_means_the_exchange_is_unavailable()
    {
        _server.RefusedTopics["allLiquidation.SOLUSDT"] = true;

        await Should.ThrowAsync<ProfitFox.Application.Exceptions.ExchangeUnavailableException>(() =>
            _streams.SubscribeLiquidationsAsync([Sol], (_, _) => { }, _ => { }, _ => { }, CancellationToken.None));
    }

    // Requirement "Разрывы потока ликвидаций": both ends of a break are told with the time of the scanner.
    [Test]
    public async Task Break_of_the_liquidation_stream_is_told_lost_then_restored()
    {
        var events = new BlockingCollection<(string, DateTimeOffset)>();
        await using IAsyncDisposable subscription = await _streams.SubscribeLiquidationsAsync(
            [Sol], (_, _) => { }, at => events.Add(("lost", at)), at => events.Add(("restored", at)), CancellationToken.None);

        await _server.DropAsync();

        events.TryTake(out (string, DateTimeOffset) first, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        events.TryTake(out (string, DateTimeOffset) second, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        first.ShouldBe(("lost", Now));
        second.ShouldBe(("restored", Now));
    }

    // A connection carries several subscriptions, and each tells its own break; the stream tells one break: lost with the first, back with the last.
    [Test]
    public async Task Break_of_several_subscriptions_is_told_once()
    {
        Instrument[] linear = [.. Enumerable.Range(0, 25).Select(i => new Instrument(MarketCategory.Linear, $"C{i:000}USDT"))];
        var lost = 0;
        var restored = new SemaphoreSlim(0);
        await using IAsyncDisposable subscription = await _streams.SubscribeLiquidationsAsync(
            linear, (_, _) => { }, _ => Interlocked.Increment(ref lost), _ => restored.Release(), CancellationToken.None);

        await _server.DropAsync();

        (await restored.WaitAsync(TimeSpan.FromSeconds(30))).ShouldBeTrue();
        (await restored.WaitAsync(TimeSpan.FromSeconds(2))).ShouldBeFalse();
        lost.ShouldBe(1);
    }

    // Requirement "Минутные суммы сделок" and decision 5 of collect-trade-flow: every trade comes with the side of its taker, a block trade and
    // a trade of the RPI orders too.
    [TestCase(MarketCategory.Linear, "Buy", TradeSide.Buy, false, false)]
    [TestCase(MarketCategory.Spot, "Sell", TradeSide.Sell, false, false)]
    [TestCase(MarketCategory.Linear, "Sell", TradeSide.Sell, true, false)]
    [TestCase(MarketCategory.Spot, "Buy", TradeSide.Buy, false, true)]
    public async Task Trade_comes_with_its_instrument_time_side_volume_and_price(MarketCategory category, string takerSide, TradeSide side, bool block, bool rpi)
    {
        var at = DateTimeOffset.Parse("2026-11-09T12:04:59.999Z", CultureInfo.InvariantCulture);
        var instrument = new Instrument(category, "SOLUSDT");
        var trades = new BlockingCollection<(Instrument, TradeEvent)>();
        await using IAsyncDisposable subscription = await _streams.SubscribeTradesAsync(
            [instrument], (traded, trade) => trades.Add((traded, trade)), _ => { }, _ => { }, CancellationToken.None);

        await _server.PushAsync("publicTrade.SOLUSDT", [FakeBybitSocketServer.Trade("SOLUSDT", takerSide, 2.5m, 150.25m, at, block, rpi)]);

        trades.TryTake(out (Instrument Instrument, TradeEvent Trade) received, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        received.ShouldBe((instrument, new TradeEvent(at, side, 2.5m, 150.25m)));
    }

    [Test]
    public async Task Trades_of_25_pairs_go_in_3_subscribe_requests()
    {
        Instrument[] linear = [.. Enumerable.Range(0, 25).Select(i => new Instrument(MarketCategory.Linear, $"C{i:000}USDT"))];

        await using IAsyncDisposable subscription = await _streams.SubscribeTradesAsync(linear, (_, _) => { }, _ => { }, _ => { }, CancellationToken.None);

        linear.ShouldAllBe(instrument => _server.Subscribed($"publicTrade.{instrument.Symbol}"));
        _server.SubscribeSizes.ShouldBe([10, 10, 5]);
    }

    [Test]
    public async Task Trades_of_linear_and_spot_go_to_their_own_streams()
    {
        await using IAsyncDisposable subscription = await _streams.SubscribeTradesAsync(
            [Sol, new Instrument(MarketCategory.Spot, "BTCUSDT")], (_, _) => { }, _ => { }, _ => { }, CancellationToken.None);

        _server.Connections("/v5/public/linear").ShouldBe(1);
        _server.Connections("/v5/public/spot").ShouldBe(1);
    }

    [Test]
    public async Task Refused_trade_subscription_means_the_exchange_is_unavailable()
    {
        _server.RefusedTopics["publicTrade.SOLUSDT"] = true;

        await Should.ThrowAsync<ProfitFox.Application.Exceptions.ExchangeUnavailableException>(() =>
            _streams.SubscribeTradesAsync([Sol], (_, _) => { }, _ => { }, _ => { }, CancellationToken.None));
    }

    // Requirement "Разрывы потока сделок": both ends of a break are told with the time of the collector.
    [Test]
    public async Task Break_of_the_trade_stream_is_told_lost_then_restored()
    {
        var events = new BlockingCollection<(string, DateTimeOffset)>();
        await using IAsyncDisposable subscription = await _streams.SubscribeTradesAsync(
            [Sol, new Instrument(MarketCategory.Spot, "BTCUSDT")], (_, _) => { }, at => events.Add(("lost", at)), at => events.Add(("restored", at)), CancellationToken.None);

        await _server.DropAsync();

        events.TryTake(out (string, DateTimeOffset) first, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        events.TryTake(out (string, DateTimeOffset) second, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        first.ShouldBe(("lost", Now));
        second.ShouldBe(("restored", Now));
        events.TryTake(out _, TimeSpan.FromSeconds(2)).ShouldBeFalse();
    }

    [Test]
    public async Task Disposing_takes_every_trade_subscription_off()
    {
        IAsyncDisposable subscription = await _streams.SubscribeTradesAsync([Sol, new Instrument(MarketCategory.Spot, "BTCUSDT")], (_, _) => { }, _ => { }, _ => { }, CancellationToken.None);

        await subscription.DisposeAsync();

        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while ((_server.Connections("/v5/public/linear") + _server.Connections("/v5/public/spot")) > 0 && DateTime.UtcNow < until)
        {
            await Task.Delay(20);
        }

        _server.Subscribed("publicTrade.SOLUSDT").ShouldBeFalse();
        _server.Subscribed("publicTrade.BTCUSDT").ShouldBeFalse();
    }
}