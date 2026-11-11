using System.Collections.Concurrent;

using Microsoft.Extensions.Logging.Abstractions;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges.Bybit;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>The Bybit streams adapter against the streams of Bybit v5 on a local port.</summary>
// After a reconnect CryptoExchange.Net closes a socket no sooner than 5 seconds after it reconnected, so every test that drops
// the connection waits that long as it leaves. Each test has a server and an adapter of its own and no database, so they run
// side by side.
[Parallelizable(ParallelScope.Children)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
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
        fill.ShouldBe(new ExchangeFill("e-1", "c-1", 0.4m, 100m, 0.02m, "USDT", Open, Linear, "x-c-1", Side: OrderSide.Buy));
        _server.Authentications.ShouldBe(1);
    }

    // Scenario "Свои TP и SL": the stop Bybit made for an entry has no client id; it names its parent, and its execution names its order.
    [Test]
    public async Task Stop_made_by_the_exchange_names_its_parent_and_its_executions_name_the_order()
    {
        var orders = new BlockingCollection<ExchangeOrder>();
        var fills = new BlockingCollection<ExchangeFill>();
        await using IAsyncDisposable subscription = await _streams.SubscribeAccountAsync(
            new ExchangeKey("key", "secret"), orders.Add, fills.Add, () => { }, CancellationToken.None);
        await _server.WaitForTopicAsync("order");
        await _server.WaitForTopicAsync("execution");

        await _server.PushAsync("order", [FakeBybitSocketServer.Order("linear", "", "Filled", 0.47m, exchangeId: "x-stop", parentClientId: "c-1", stopOrderType: "PartialStopLoss")]);
        await _server.PushAsync("execution", [FakeBybitSocketServer.Execution("linear", "", "e-9", 0.47m, 99m, 0.02m, "USDT", Open, exchangeId: "x-stop")]);

        orders.TryTake(out ExchangeOrder? order, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        order!.ClientId.ShouldBe("");
        order.ParentClientId.ShouldBe("c-1");
        order.StopKind.ShouldBe(StopOrderKind.StopLoss);
        fills.TryTake(out ExchangeFill? fill, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        fill!.ExchangeOrderId.ShouldBe("x-stop");
    }

    [Test]
    public async Task Order_with_no_parent_is_no_stop()
    {
        var orders = new BlockingCollection<ExchangeOrder>();
        await using IAsyncDisposable subscription = await _streams.SubscribeAccountAsync(
            new ExchangeKey("key", "secret"), orders.Add, _ => { }, () => { }, CancellationToken.None);
        await _server.WaitForTopicAsync("order");

        await _server.PushAsync("order", [FakeBybitSocketServer.Order("linear", "c-1", "New", 0m)]);

        orders.TryTake(out ExchangeOrder? order, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        (order!.ParentClientId, order.StopKind).ShouldBe((null, StopOrderKind.None));
    }

    // Scenario "Сверка после чужого исполнения": a fill carries the instrument it was made on; a market the system does not trade has none.
    [TestCase("linear", true)]
    [TestCase("inverse", false)]
    public async Task Fill_carries_its_instrument_when_the_system_trades_that_market(string category, bool traded)
    {
        var fills = new BlockingCollection<ExchangeFill>();
        await using IAsyncDisposable subscription = await _streams.SubscribeAccountAsync(
            new ExchangeKey("key", "secret"), _ => { }, fills.Add, () => { }, CancellationToken.None);
        await _server.WaitForTopicAsync("execution");

        await _server.PushAsync("execution", [FakeBybitSocketServer.Execution(category, "c-1", "e-1", 0.4m, 100m, 0.02m, "USDT", Open)]);

        fills.TryTake(out ExchangeFill? fill, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        fill!.Instrument.ShouldBe(traded ? Linear : null);
    }

    // Requirement "Ликвидация и ADL": a fill of the stream tells a liquidation or an auto-deleveraging of Bybit, which has no client id, and its side.
    [TestCase("BustTrade", "Sell", ExchangeFillKind.Liquidation, OrderSide.Sell)]
    [TestCase("AdlTrade", "Buy", ExchangeFillKind.Adl, OrderSide.Buy)]
    [TestCase("Trade", "Sell", ExchangeFillKind.Trade, OrderSide.Sell)]
    public async Task Fill_tells_its_kind_and_its_side(string type, string side, ExchangeFillKind kind, OrderSide orderSide)
    {
        var fills = new BlockingCollection<ExchangeFill>();
        await using IAsyncDisposable subscription = await _streams.SubscribeAccountAsync(
            new ExchangeKey("key", "secret"), _ => { }, fills.Add, () => { }, CancellationToken.None);
        await _server.WaitForTopicAsync("execution");

        await _server.PushAsync("execution", [FakeBybitSocketServer.Execution("linear", "", "e-1", 1100m, 0.014229m, 0.0086m, "USDT", Open, exchangeId: "x-bust", type: type, side: side)]);

        fills.TryTake(out ExchangeFill? fill, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        (fill!.Kind, fill.Side).ShouldBe((kind, orderSide));
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

    // A bot that leaves cancels the token it subscribed with: the subscription it opened stays for the others of the pair.
    [Test]
    public async Task Cancelled_token_of_the_first_subscriber_keeps_the_candles_of_the_others()
    {
        var candles = new BlockingCollection<Candle>();
        using var first = new CancellationTokenSource();
        IAsyncDisposable opened = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, _ => { }, () => { }, first.Token);
        await using IAsyncDisposable second = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, candles.Add, () => { }, CancellationToken.None);

        await first.CancelAsync();
        await opened.DisposeAsync();
        await Task.Delay(200);

        _server.Subscribed("kline.60.BTCUSDT").ShouldBeTrue();
        await _server.PushAsync("kline.60.BTCUSDT", [FakeBybitSocketServer.Kline(Open, TimeSpan.FromHours(1), 102m, confirm: true)]);
        candles.TryTake(out _, TimeSpan.FromSeconds(10)).ShouldBeTrue();
    }

    [Test]
    public async Task Cancelled_token_of_the_first_subscriber_keeps_the_account_stream_of_the_others()
    {
        var orders = new BlockingCollection<ExchangeOrder>();
        var fills = new BlockingCollection<ExchangeFill>();
        using var first = new CancellationTokenSource();
        IAsyncDisposable opened = await _streams.SubscribeAccountAsync(new ExchangeKey("key", "secret"), _ => { }, _ => { }, () => { }, first.Token);
        await using IAsyncDisposable second = await _streams.SubscribeAccountAsync(new ExchangeKey("key", "secret"), orders.Add, fills.Add, () => { }, CancellationToken.None);

        await first.CancelAsync();
        await opened.DisposeAsync();
        await Task.Delay(200);

        await _server.PushAsync("order", [FakeBybitSocketServer.Order("linear", "c-1", "PartiallyFilled", 0.4m)]);
        await _server.PushAsync("execution", [FakeBybitSocketServer.Execution("linear", "c-1", "e-1", 0.4m, 100m, 0.02m, "USDT", Open)]);
        orders.TryTake(out _, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        fills.TryTake(out _, TimeSpan.FromSeconds(10)).ShouldBeTrue();
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

    // Requirement "Текущая свеча": the listeners of updates see every update of the topic, the closed ones only the confirmed.
    [Test]
    public async Task Update_listener_gets_the_forming_candle_and_its_closing_while_a_closed_listener_gets_only_the_closing()
    {
        var updates = new BlockingCollection<FormingCandle>();
        var closed = new BlockingCollection<Candle>();
        await using IAsyncDisposable forming = await Forming().SubscribeAsync(Linear, Interval.OneHour, updates.Add, () => { }, () => { }, () => { }, CancellationToken.None);
        await using IAsyncDisposable candles = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, closed.Add, () => { }, CancellationToken.None);
        await _server.WaitForTopicAsync("kline.60.BTCUSDT");
        DateTimeOffset at = Open.AddMinutes(10);

        await _server.PushAsync("kline.60.BTCUSDT", [FakeBybitSocketServer.Kline(Open, TimeSpan.FromHours(1), 101m, confirm: false, volume: 2.5m, at: at)]);
        await _server.PushAsync("kline.60.BTCUSDT", [FakeBybitSocketServer.Kline(Open, TimeSpan.FromHours(1), 102m, confirm: true, volume: 3m, at: at.AddMinutes(50))]);

        updates.TryTake(out FormingCandle? first, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        updates.TryTake(out FormingCandle? second, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        (first!.OpenTime, first.Close.Value, first.Volume.Value, first.IsClosed, first.ObservedAt).ShouldBe((Open, 101m, 2.5m, false, at));
        (second!.Close.Value, second.Volume.Value, second.IsClosed, second.ObservedAt).ShouldBe((102m, 3m, true, at.AddMinutes(50)));
        closed.TryTake(out Candle? candle, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        candle!.Close.Value.ShouldBe(102m);
        closed.TryTake(out _, TimeSpan.FromMilliseconds(200)).ShouldBeFalse();
    }

    // Requirement "Одна подписка на серию": a listener of either kind joins the one subscription of the topic.
    [Test]
    public async Task Listeners_of_both_kinds_share_one_subscription_and_the_last_to_leave_takes_it_off()
    {
        IAsyncDisposable forming = await Forming().SubscribeAsync(Linear, Interval.OneHour, _ => { }, () => { }, () => { }, () => { }, CancellationToken.None);
        IAsyncDisposable candles = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, _ => { }, () => { }, CancellationToken.None);
        await _server.WaitForTopicAsync("kline.60.BTCUSDT");

        _server.Subscribes("kline.60.BTCUSDT").ShouldBe(1);
        await forming.DisposeAsync();
        await Task.Delay(200);
        _server.Subscribed("kline.60.BTCUSDT").ShouldBeTrue();
        await candles.DisposeAsync();
        await WaitAsync(() => !_server.Subscribed("kline.60.BTCUSDT"));
    }

    [Test]
    public async Task Update_listener_is_the_last_to_leave_and_takes_the_topic_off()
    {
        IAsyncDisposable candles = await _streams.SubscribeCandlesAsync(Linear, Interval.OneHour, _ => { }, () => { }, CancellationToken.None);
        IAsyncDisposable forming = await Forming().SubscribeAsync(Linear, Interval.OneHour, _ => { }, () => { }, () => { }, () => { }, CancellationToken.None);
        await _server.WaitForTopicAsync("kline.60.BTCUSDT");

        await candles.DisposeAsync();
        await Task.Delay(200);
        _server.Subscribed("kline.60.BTCUSDT").ShouldBeTrue();
        await forming.DisposeAsync();
        await WaitAsync(() => !_server.Subscribed("kline.60.BTCUSDT"));
    }

    // Requirement "Состояние канала текущей свечи": a broken connection is told as lost, then as restored.
    [Test]
    public async Task Drop_is_told_as_lost_and_then_restored()
    {
        var events = new BlockingCollection<string>();
        await using IAsyncDisposable subscription = await Forming().SubscribeAsync(
            Linear,
            Interval.OneHour,
            _ => { },
            () => events.Add("lost"),
            () => events.Add("restored"),
            () => { },
            CancellationToken.None);
        await _server.WaitForTopicAsync("kline.60.BTCUSDT");

        await _server.DropAsync();

        events.TryTake(out string? first, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        events.TryTake(out string? second, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        (first, second).ShouldBe(("lost", "restored"));
    }

    // An unsubscribed subscription must not speak for the topic's next one on the same connection.
    [Test]
    public async Task A_subscription_that_was_taken_off_does_not_report_a_drop_to_the_next_one()
    {
        IAsyncDisposable first = await Forming().SubscribeAsync(Linear, Interval.OneHour, _ => { }, () => { }, () => { }, () => { }, CancellationToken.None);
        await _server.WaitForTopicAsync("kline.60.BTCUSDT");
        // Another topic keeps the connection open while the first one is taken off and made again.
        await using IAsyncDisposable other = await Forming().SubscribeAsync(Linear, Interval.FifteenMinutes, _ => { }, () => { }, () => { }, () => { }, CancellationToken.None);
        await first.DisposeAsync();
        await WaitAsync(() => !_server.Subscribed("kline.60.BTCUSDT"));
        var lost = 0;
        var restored = new SemaphoreSlim(0);
        await using IAsyncDisposable second = await Forming().SubscribeAsync(
            Linear,
            Interval.OneHour,
            _ => { },
            () => Interlocked.Increment(ref lost),
            () => restored.Release(),
            () => { },
            CancellationToken.None);
        await _server.WaitForTopicAsync("kline.60.BTCUSDT");

        await _server.DropAsync();

        (await restored.WaitAsync(TimeSpan.FromSeconds(30))).ShouldBeTrue();
        await Task.Delay(200);
        lost.ShouldBe(1);
    }

    // Decision 10 of collect-trade-flow: the trades of a pair for its current bar, on the public client the API shares with the candles.
    [TestCase(MarketCategory.Linear, "Buy", TradeSide.Buy)]
    [TestCase(MarketCategory.Spot, "Sell", TradeSide.Sell)]
    public async Task Trades_of_a_pair_come_with_their_time_side_volume_and_price(MarketCategory category, string takerSide, TradeSide side)
    {
        var at = DateTimeOffset.Parse("2026-11-11T12:04:30.250Z", System.Globalization.CultureInfo.InvariantCulture);
        var trades = new BlockingCollection<TradeEvent>();
        await using IAsyncDisposable subscription = await TradeFlow().SubscribeTradesAsync(
            new Instrument(category, "SOLUSDT"), trades.Add, () => { }, () => { }, () => { }, CancellationToken.None);

        await _server.PushAsync("publicTrade.SOLUSDT", [FakeBybitSocketServer.Trade("SOLUSDT", takerSide, 1.5m, 150.5m, at)]);

        trades.TryTake(out TradeEvent? trade, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        trade.ShouldBe(new TradeEvent(at, side, 1.5m, 150.5m));
    }

    [Test]
    public async Task Trades_and_candles_of_a_pair_share_the_public_connection()
    {
        await using IAsyncDisposable candles = await Forming().SubscribeAsync(Linear, Interval.OneHour, _ => { }, () => { }, () => { }, () => { }, CancellationToken.None);
        await using IAsyncDisposable trades = await TradeFlow().SubscribeTradesAsync(Linear, _ => { }, () => { }, () => { }, () => { }, CancellationToken.None);

        _server.Subscribed("publicTrade.BTCUSDT").ShouldBeTrue();
        _server.Connections("/v5/public/linear").ShouldBe(1);
    }

    [Test]
    public async Task Refused_trade_topic_is_unavailable()
    {
        _server.RefusedTopics["publicTrade.BTCUSDT"] = true;

        await Should.ThrowAsync<ExchangeUnavailableException>(() =>
            TradeFlow().SubscribeTradesAsync(Linear, _ => { }, () => { }, () => { }, () => { }, CancellationToken.None));
    }

    [Test]
    public async Task Drop_of_the_trades_is_told_as_lost_and_then_restored()
    {
        var events = new BlockingCollection<string>();
        await using IAsyncDisposable subscription = await TradeFlow().SubscribeTradesAsync(
            Linear, _ => { }, () => events.Add("lost"), () => events.Add("restored"), () => { }, CancellationToken.None);

        await _server.DropAsync();

        events.TryTake(out string? first, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        events.TryTake(out string? second, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        (first, second).ShouldBe(("lost", "restored"));
    }

    [Test]
    public async Task Disposing_takes_the_trade_topic_off()
    {
        await using IAsyncDisposable candles = await Forming().SubscribeAsync(Linear, Interval.OneHour, _ => { }, () => { }, () => { }, () => { }, CancellationToken.None);
        IAsyncDisposable trades = await TradeFlow().SubscribeTradesAsync(Linear, _ => { }, () => { }, () => { }, () => { }, CancellationToken.None);

        await trades.DisposeAsync();

        await WaitAsync(() => !_server.Subscribed("publicTrade.BTCUSDT"));
        _server.Subscribed("kline.60.BTCUSDT").ShouldBeTrue();
    }

    private ITradeFlowStreams TradeFlow()
    {
        return _streams;
    }

    private IFormingCandleStreams Forming()
    {
        return _streams;
    }
}