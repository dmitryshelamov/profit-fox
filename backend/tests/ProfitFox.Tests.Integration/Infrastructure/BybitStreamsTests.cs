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

    private static async Task WaitAsync(Func<bool> condition)
    {
        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The condition did not hold in time.");
            }

            await Task.Delay(20);
        }
    }

    private static async Task DisposeAllAsync(IEnumerable<IAsyncDisposable> subscriptions)
    {
        foreach (IAsyncDisposable subscription in subscriptions)
        {
            await subscription.DisposeAsync();
        }
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

    // Requirement "Соединения с биржей": the bots of an account share its private connection and pick their own events.
    [Test]
    public async Task Subscribers_of_one_account_share_one_private_connection()
    {
        BlockingCollection<ExchangeOrder>[] orders = Enumerable.Range(0, 3).Select(_ => new BlockingCollection<ExchangeOrder>()).ToArray();
        BlockingCollection<ExchangeFill>[] fills = Enumerable.Range(0, 3).Select(_ => new BlockingCollection<ExchangeFill>()).ToArray();
        var subscriptions = new List<IAsyncDisposable>();

        for (var i = 0; i < 3; i++)
        {
            subscriptions.Add(await _streams.SubscribeAccountAsync(new ExchangeKey("key", "secret"), orders[i].Add, fills[i].Add, () => { }, CancellationToken.None));
        }

        await _server.PushAsync("order", [FakeBybitSocketServer.Order("linear", "c-1", "PartiallyFilled", 0.4m)]);
        await _server.PushAsync("execution", [FakeBybitSocketServer.Execution("linear", "c-1", "e-1", 0.4m, 100m, 0.02m, "USDT", Open)]);

        for (var i = 0; i < 3; i++)
        {
            orders[i].TryTake(out _, TimeSpan.FromSeconds(10)).ShouldBeTrue();
            fills[i].TryTake(out _, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        }

        (_server.Connections("/v5/private"), _server.Authentications, _server.Subscribes("order"), _server.Subscribes("execution")).ShouldBe((1, 1, 1, 1));
        await DisposeAllAsync(subscriptions);
        await WaitAsync(() => _server.Connections("/v5/private") == 0);
    }

    // A refused subscription is the exchange being unavailable to the session, whichever of the two topics it is.
    [TestCase("order")]
    [TestCase("execution")]
    public async Task Refused_account_topic_is_unavailable(string topic)
    {
        _server.RefusedTopics[topic] = true;

        await Should.ThrowAsync<ProfitFox.Application.Exceptions.ExchangeUnavailableException>(() =>
            _streams.SubscribeAccountAsync(new ExchangeKey("key", "secret"), _ => { }, _ => { }, () => { }, CancellationToken.None));
        await WaitAsync(() => _server.Connections("/v5/private") == 0);
    }

    // The subscriber that opened a refused subscription is told; the next one asks Bybit again.
    [Test]
    public async Task After_a_refusal_the_next_subscriber_subscribes_again()
    {
        var candles = new BlockingCollection<Candle>();
        _server.RefusedTopics["kline.60.BTCUSDT"] = true;
        await Should.ThrowAsync<ProfitFox.Application.Exceptions.ExchangeUnavailableException>(() =>
            _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, _ => { }, () => { }, CancellationToken.None));
        _server.RefusedTopics.Clear();

        await using IAsyncDisposable subscription = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, candles.Add, () => { }, CancellationToken.None);

        await _server.WaitForTopicAsync("kline.60.BTCUSDT");
        await _server.PushAsync("kline.60.BTCUSDT", [FakeBybitSocketServer.Kline(Open, TimeSpan.FromHours(1), 102m, confirm: true)]);
        candles.TryTake(out _, TimeSpan.FromSeconds(10)).ShouldBeTrue();
    }

    // Scenario "Обрыв публичного соединения".
    [Test]
    public async Task Drop_tells_every_subscriber_of_the_connection()
    {
        var restored = new SemaphoreSlim(0);
        var eth = new Instrument(MarketCategory.Linear, "ETHUSDT");
        await using IAsyncDisposable one = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, _ => { }, () => restored.Release(), CancellationToken.None);
        await using IAsyncDisposable two = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, _ => { }, () => restored.Release(), CancellationToken.None);
        await using IAsyncDisposable three = await _streams.SubscribeCandlesAsync(eth, Interval.OneHour, _ => { }, () => restored.Release(), CancellationToken.None);

        await _server.DropAsync();

        for (var i = 0; i < 3; i++)
        {
            (await restored.WaitAsync(TimeSpan.FromSeconds(30))).ShouldBeTrue();
        }
    }

    // Requirement "Соединения с биржей": the connections follow the pairs, not the bots.
    [Test]
    public async Task Ten_subscribers_on_three_pairs_share_one_connection()
    {
        var eth = new Instrument(MarketCategory.Linear, "ETHUSDT");
        (Instrument, Interval)[] pairs = [(Linear, Interval.OneHour), (Linear, Interval.FifteenMinutes), (eth, Interval.OneHour)];
        var subscriptions = new List<IAsyncDisposable>();

        for (var i = 0; i < 10; i++)
        {
            (Instrument instrument, Interval interval) = pairs[i % pairs.Length];
            subscriptions.Add(await _streams.SubscribeCandlesAsync(instrument, interval, _ => { }, () => { }, CancellationToken.None));
        }

        _server.Connections("/v5/public/linear").ShouldBe(1);
        (_server.Subscribes("kline.60.BTCUSDT"), _server.Subscribes("kline.15.BTCUSDT"), _server.Subscribes("kline.60.ETHUSDT")).ShouldBe((1, 1, 1));
        await DisposeAllAsync(subscriptions);
    }

    // Scenario "Два бота на одной паре".
    [Test]
    public async Task Two_subscribers_of_one_pair_get_the_candle_from_one_subscription()
    {
        var first = new BlockingCollection<Candle>();
        var second = new BlockingCollection<Candle>();
        await using IAsyncDisposable one = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, first.Add, () => { }, CancellationToken.None);
        await using IAsyncDisposable two = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, second.Add, () => { }, CancellationToken.None);

        await _server.PushAsync("kline.60.BTCUSDT", [FakeBybitSocketServer.Kline(Open, TimeSpan.FromHours(1), 102m, confirm: true)]);

        first.TryTake(out Candle? candle, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        candle!.Close.Value.ShouldBe(102m);
        second.TryTake(out candle, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        candle!.Close.Value.ShouldBe(102m);
        _server.Subscribes("kline.60.BTCUSDT").ShouldBe(1);
    }

    // Scenario "Спот и линейные": Bybit serves the two categories at different addresses.
    [Test]
    public async Task Spot_and_linear_take_a_connection_each()
    {
        var spot = new Instrument(MarketCategory.Spot, "SOLUSDT");
        await using IAsyncDisposable one = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, _ => { }, () => { }, CancellationToken.None);
        await using IAsyncDisposable two = await _streams.SubscribeCandlesAsync(spot, Interval.OneHour, _ => { }, () => { }, CancellationToken.None);
        await using IAsyncDisposable three = await _streams.SubscribeCandlesAsync(spot, Interval.FifteenMinutes, _ => { }, () => { }, CancellationToken.None);

        (_server.Connections("/v5/public/linear"), _server.Connections("/v5/public/spot")).ShouldBe((1, 1));
    }

    // Scenario "Последний бот на паре остановлен".
    [Test]
    public async Task Last_subscriber_to_leave_takes_the_topic_off()
    {
        var eth = new Instrument(MarketCategory.Linear, "ETHUSDT");
        var candles = new BlockingCollection<Candle>();
        IAsyncDisposable alone = await _streams.SubscribeCandlesAsync(eth, Interval.FifteenMinutes, _ => { }, () => { }, CancellationToken.None);
        IAsyncDisposable first = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, _ => { }, () => { }, CancellationToken.None);
        await using IAsyncDisposable second = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, candles.Add, () => { }, CancellationToken.None);

        await alone.DisposeAsync();
        await first.DisposeAsync();

        await WaitAsync(() => !_server.Subscribed("kline.15.ETHUSDT"));
        _server.Subscribed("kline.60.BTCUSDT").ShouldBeTrue();
        await _server.PushAsync("kline.60.BTCUSDT", [FakeBybitSocketServer.Kline(Open, TimeSpan.FromHours(1), 102m, confirm: true)]);
        candles.TryTake(out _, TimeSpan.FromSeconds(10)).ShouldBeTrue();
    }

    [Test]
    public async Task Connection_without_subscriptions_closes()
    {
        IAsyncDisposable subscription = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, _ => { }, () => { }, CancellationToken.None);

        await subscription.DisposeAsync();

        await WaitAsync(() => _server.Connections("/v5/public/linear") == 0);
    }

    // Bots of one pair start at once: the pair is still subscribed once.
    [Test]
    public async Task Subscribers_of_one_pair_at_once_make_one_subscription()
    {
        IAsyncDisposable[] subscriptions = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, _ => { }, () => { }, CancellationToken.None)));

        _server.Subscribes("kline.60.BTCUSDT").ShouldBe(1);
        await DisposeAllAsync(subscriptions);
    }

    // The last bot of a pair leaves as another comes: the one that came gets the candles.
    [Test]
    public async Task Subscriber_that_comes_as_the_last_leaves_gets_the_candles()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candles = new BlockingCollection<Candle>();
            IAsyncDisposable leaving = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, _ => { }, () => { }, CancellationToken.None);

            Task<IAsyncDisposable> coming = _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, candles.Add, () => { }, CancellationToken.None);
            await leaving.DisposeAsync();
            await using IAsyncDisposable came = await coming;

            await _server.WaitForTopicAsync("kline.60.BTCUSDT");
            await _server.PushAsync("kline.60.BTCUSDT", [FakeBybitSocketServer.Kline(Open, TimeSpan.FromHours(1), 102m, confirm: true)]);
            candles.TryTake(out _, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        }
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