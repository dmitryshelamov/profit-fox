using System.Collections.Concurrent;

using Microsoft.Extensions.Logging.Abstractions;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges.Bybit;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>The Bybit streams adapter against the streams of Bybit v5 on a local port.</summary>
public class BybitStreamsTests
{
    private static readonly DateTimeOffset Open = DateTimeOffset.Parse("2024-03-10T12:00:00Z");
    private static readonly Instrument Linear = new Instrument(MarketCategory.Linear, "BTCUSDT");

    private FakeBybitSocketServer _server = null!;
    private BybitStreams _streams = null!;

    [SetUp]
    public async Task SetUp()
    {
        _server = await FakeBybitSocketServer.StartAsync();
        _streams = new BybitStreams(NullLoggerFactory.Instance, new BybitSettings(SocketAddress: _server.Address));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _server.DisposeAsync();
    }

    // Requirement "Свечи запущенного бота": a candle that is still forming never reaches the strategy.
    [Test]
    public async Task Only_closed_candles_come()
    {
        var candles = new BlockingCollection<Candle>();
        await using IAsyncDisposable subscription = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, candles.Add, () => { }, CancellationToken.None);
        await _server.WaitForTopicAsync("kline.60.BTCUSDT");

        await _server.PushAsync("kline.60.BTCUSDT", [FakeBybitSocketServer.Kline(Open, TimeSpan.FromHours(1), 101m, confirm: false)]);
        await _server.PushAsync("kline.60.BTCUSDT", [FakeBybitSocketServer.Kline(Open, TimeSpan.FromHours(1), 102m, confirm: true)]);

        candles.TryTake(out Candle? candle, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        (candle!.OpenTime, candle.Close.Value).ShouldBe((Open, 102m));
        candles.TryTake(out _, TimeSpan.FromMilliseconds(200)).ShouldBeFalse();
    }

    [Test]
    public async Task Account_stream_brings_orders_and_fills()
    {
        var orders = new BlockingCollection<ExchangeOrder>();
        var fills = new BlockingCollection<ExchangeFill>();
        await using IAsyncDisposable subscription = await _streams.SubscribeAccountAsync(
            new ExchangeKey("key", "secret"), orders.Add, fills.Add, () => { }, CancellationToken.None);
        await _server.WaitForTopicAsync("order");
        await _server.WaitForTopicAsync("execution");

        await _server.PushAsync("order", [FakeBybitSocketServer.Order("linear", "c-1", "PartiallyFilled", 0.4m)]);
        await _server.PushAsync("execution", [FakeBybitSocketServer.Execution("linear", "c-1", "e-1", 0.4m, 100m, 0.02m, "USDT", Open)]);

        orders.TryTake(out ExchangeOrder? order, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        order.ShouldBe(new ExchangeOrder("c-1", "x-c-1", ExchangeOrderState.Open, 0.4m, null, 40m, 0.02m, "USDT"));
        fills.TryTake(out ExchangeFill? fill, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        fill.ShouldBe(new ExchangeFill("e-1", "c-1", 0.4m, 100m, 0.02m, "USDT", Open));
        _server.Authentications.ShouldBe(1);
    }

    // A refused subscription is the exchange being unavailable to the session, whichever of the two topics it is.
    [TestCase("order")]
    [TestCase("execution")]
    public async Task Refused_account_topic_is_unavailable(string topic)
    {
        _server.RefusedTopics[topic] = true;

        await Should.ThrowAsync<ProfitFox.Application.Exceptions.ExchangeUnavailableException>(() =>
            _streams.SubscribeAccountAsync(new ExchangeKey("key", "secret"), _ => { }, _ => { }, () => { }, CancellationToken.None));
    }

    // Requirement "Догрузка пропущенного": a stream that is back tells it, so the missed events are read again.
    [Test]
    public async Task Restored_connection_is_told()
    {
        var restored = new SemaphoreSlim(0);
        await using IAsyncDisposable subscription = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, _ => { }, () => restored.Release(), CancellationToken.None);
        await _server.WaitForTopicAsync("kline.60.BTCUSDT");

        await _server.DropAsync();

        (await restored.WaitAsync(TimeSpan.FromSeconds(30))).ShouldBeTrue();
    }
}