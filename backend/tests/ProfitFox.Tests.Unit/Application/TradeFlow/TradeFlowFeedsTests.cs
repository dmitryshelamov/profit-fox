using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.MarketData.FormingCandles;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.TradeFlow.Live;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.TradeFlow;

/// <summary>Requirements "Текущий бар потока сделок" and "Состояние канала текущего бара" (decision 10 of collect-trade-flow).</summary>
public class TradeFlowFeedsTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-11T12:04:30Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset Minute = DateTimeOffset.Parse("2026-11-11T12:04:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Spot, "BTCUSDT");

    private FakeTimeProvider _time = null!;
    private ITradeFlowStreams _streams = null!;
    private ITradeFlowBroadcast _broadcast = null!;
    private List<Subscription?> _subscriptions = null!;
    private List<LiveTradeFlow> _sent = null!;
    private List<FormingState> _states = null!;
    private int _failures;
    private Func<Task>? _whileSubscribing;
    private TradeFlowFeeds _feeds = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Start);
        _subscriptions = [];
        _sent = [];
        _states = [];
        _failures = 0;
        _broadcast = Substitute.For<ITradeFlowBroadcast>();
        _broadcast.MinuteAsync(default!, default!).ReturnsForAnyArgs(call =>
        {
            _sent.Add(call.ArgAt<LiveTradeFlow>(1));

            return Task.CompletedTask;
        });
        _broadcast.StatusAsync(default!, default).ReturnsForAnyArgs(call =>
        {
            _states.Add(call.ArgAt<FormingState>(1));

            return Task.CompletedTask;
        });
        _streams = Substitute.For<ITradeFlowStreams>();
        _streams.SubscribeTradesAsync(default!, default!, default!, default!, default!, default).ReturnsForAnyArgs(call =>
        {
            if (_failures > 0)
            {
                _failures--;
                _subscriptions.Add(null);

                return Task.FromException<IAsyncDisposable>(new ExchangeUnavailableException("down"));
            }

            var subscription = new Subscription(call.ArgAt<Instrument>(0), call.ArgAt<Action<TradeEvent>>(1), call.ArgAt<Action>(2), call.ArgAt<Action>(4));
            _subscriptions.Add(subscription);

            return _whileSubscribing is { } meanwhile ? meanwhile().ContinueWith<IAsyncDisposable>(_ => subscription, TaskScheduler.Default) : Task.FromResult<IAsyncDisposable>(subscription);
        });
        _feeds = new TradeFlowFeeds(_streams, _broadcast, _time);
    }

    [Test]
    public async Task First_viewer_subscribes_the_trades_of_the_pair_and_waits_for_the_first_trade()
    {
        LiveTradeFlowSnapshot snapshot = await _feeds.JoinAsync("a", Sol, CancellationToken.None);

        snapshot.ShouldBe(new LiveTradeFlowSnapshot(FormingState.Connecting, null));
        _subscriptions.Single()!.Instrument.ShouldBe(Sol);
    }

    // Scenario "Сделки идут": a trade every 100 ms, at most one update a second, each with every trade of the minute since the subscription.
    [Test]
    public async Task Trades_go_out_at_most_once_a_second_with_every_trade_of_the_minute_since_the_subscription()
    {
        await _feeds.JoinAsync("a", Sol, CancellationToken.None);
        Subscription subscription = _subscriptions.Single()!;

        for (var i = 0; i < 25; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(100));
            subscription.Traded(new TradeEvent(_time.GetUtcNow(), TradeSide.Buy, 1m, 10m));
        }

        _time.Advance(TimeSpan.FromSeconds(1));

        _sent.Count.ShouldBe(4);
        _sent[^1].ShouldBe(new LiveTradeFlow(new TradeFlowMinute(Minute, 25m, 0m, 250m, 0m, 25, 0), Start));
        _states.ShouldBe([FormingState.Live]);
        (await _feeds.JoinAsync("b", Sol, CancellationToken.None)).ShouldBe(new LiveTradeFlowSnapshot(FormingState.Live, _sent[^1]));
    }

    // Decision 10: the sums start again at the boundary of the minute, and the new minute goes at once.
    [Test]
    public async Task New_minute_starts_from_zero_and_goes_at_once()
    {
        await _feeds.JoinAsync("a", Sol, CancellationToken.None);
        Subscription subscription = _subscriptions.Single()!;
        subscription.Traded(new TradeEvent(Minute.AddSeconds(59.9), TradeSide.Buy, 1m, 10m));
        subscription.Traded(new TradeEvent(Minute.AddSeconds(59.95), TradeSide.Buy, 1m, 10m));

        subscription.Traded(new TradeEvent(Minute.AddSeconds(60.1), TradeSide.Sell, 2m, 11m));

        _sent.Count.ShouldBe(2);
        _sent[^1].ShouldBe(new LiveTradeFlow(new TradeFlowMinute(Minute.AddMinutes(1), 0m, 2m, 0m, 22m, 0, 1), Start));
    }

    [Test]
    public async Task Trade_of_a_minute_already_past_is_dropped()
    {
        await _feeds.JoinAsync("a", Sol, CancellationToken.None);
        Subscription subscription = _subscriptions.Single()!;
        subscription.Traded(new TradeEvent(Minute.AddSeconds(61), TradeSide.Buy, 1m, 10m));

        subscription.Traded(new TradeEvent(Minute.AddSeconds(59), TradeSide.Buy, 5m, 10m));
        _time.Advance(TimeSpan.FromSeconds(2));

        _sent.ShouldBe([new LiveTradeFlow(new TradeFlowMinute(Minute.AddMinutes(1), 1m, 0m, 10m, 0m, 1, 0), Start)]);
    }

    // Scenario "Две вкладки".
    [Test]
    public async Task Second_viewer_of_the_pair_joins_the_subscription_of_the_first()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        await _feeds.JoinAsync("b", Btc, CancellationToken.None);

        _subscriptions.Count.ShouldBe(1);
    }

    // Scenario "Последняя вкладка закрыта".
    [Test]
    public async Task Last_viewer_to_leave_takes_the_subscription_off()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        await _feeds.JoinAsync("b", Btc, CancellationToken.None);

        await _feeds.LeaveAsync("a");
        int afterFirst = _subscriptions[0]!.Disposed;
        await _feeds.LeaveAsync("b");

        afterFirst.ShouldBe(0);
        _subscriptions[0]!.Disposed.ShouldBe(1);
    }

    // A connection follows one pair at a time.
    [Test]
    public async Task Viewer_that_opens_another_pair_leaves_the_first()
    {
        await _feeds.JoinAsync("a", Sol, CancellationToken.None);

        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);

        _subscriptions.Count.ShouldBe(2);
        _subscriptions[0]!.Disposed.ShouldBe(1);
        _subscriptions[1]!.Disposed.ShouldBe(0);
    }

    // Scenario "Связь потеряна".
    [Test]
    public async Task Lost_connection_has_no_data_until_the_next_trade()
    {
        await _feeds.JoinAsync("a", Sol, CancellationToken.None);
        Subscription subscription = _subscriptions.Single()!;
        subscription.Traded(new TradeEvent(Start, TradeSide.Buy, 1m, 10m));

        subscription.Lost();
        subscription.Traded(new TradeEvent(Start.AddSeconds(5), TradeSide.Buy, 1m, 10m));

        _states.ShouldBe([FormingState.Live, FormingState.NoData, FormingState.Live]);
    }

    // Scenario "Тихая пара".
    [Test]
    public async Task Pair_silent_for_90_seconds_has_no_data_until_the_next_trade()
    {
        await _feeds.JoinAsync("a", Sol, CancellationToken.None);
        Subscription subscription = _subscriptions.Single()!;
        subscription.Traded(new TradeEvent(Start, TradeSide.Buy, 1m, 10m));

        _time.Advance(TimeSpan.FromSeconds(89));
        List<FormingState> before = [.. _states];
        _time.Advance(TimeSpan.FromSeconds(1));
        subscription.Traded(new TradeEvent(_time.GetUtcNow(), TradeSide.Buy, 1m, 10m));

        before.ShouldBe([FormingState.Live]);
        _states.ShouldBe([FormingState.Live, FormingState.NoData, FormingState.Live]);
    }

    [Test]
    public async Task Pair_without_any_trade_for_90_seconds_has_no_data()
    {
        await _feeds.JoinAsync("a", Sol, CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(90));

        _states.ShouldBe([FormingState.NoData]);
    }

    // A refused or closed subscription has no data and is made again.
    [Test]
    public async Task Refused_subscription_has_no_data_and_is_made_again()
    {
        _failures = 1;

        await _feeds.JoinAsync("a", Sol, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(5));

        _states.ShouldBe([FormingState.NoData, FormingState.Connecting]);
        _subscriptions.Count.ShouldBe(2);
        _subscriptions[1].ShouldNotBeNull();
    }

    [Test]
    public async Task Closed_subscription_has_no_data_and_is_made_again()
    {
        await _feeds.JoinAsync("a", Sol, CancellationToken.None);

        _subscriptions[0]!.Closed();
        _time.Advance(TimeSpan.FromSeconds(5));

        _states.ShouldBe([FormingState.NoData, FormingState.Connecting]);
        _subscriptions.Count.ShouldBe(2);
        _subscriptions[0]!.Disposed.ShouldBe(1);
    }

    [Test]
    public async Task Feed_closed_while_its_retry_waits_subscribes_no_more()
    {
        _failures = 1;
        await _feeds.JoinAsync("a", Sol, CancellationToken.None);

        await _feeds.LeaveAsync("a");
        _time.Advance(TimeSpan.FromSeconds(5));

        _subscriptions.Count.ShouldBe(1);
    }

    // The sums of a subscription made again start over from its own moment, and the subscription it replaced says nothing any more.
    [Test]
    public async Task Subscription_made_again_starts_the_sums_over_and_the_one_it_replaced_says_nothing()
    {
        await _feeds.JoinAsync("a", Sol, CancellationToken.None);
        Subscription first = _subscriptions[0]!;
        first.Traded(new TradeEvent(Start, TradeSide.Buy, 1m, 10m));
        first.Closed();
        _time.Advance(TimeSpan.FromSeconds(5));
        Subscription second = _subscriptions[1]!;
        _states.Clear();

        first.Traded(new TradeEvent(_time.GetUtcNow(), TradeSide.Buy, 7m, 10m));
        first.Lost();
        first.Closed();
        second.Traded(new TradeEvent(_time.GetUtcNow(), TradeSide.Sell, 1m, 10m));

        _sent[^1].ShouldBe(new LiveTradeFlow(new TradeFlowMinute(Minute, 0m, 1m, 0m, 10m, 0, 1), Start.AddSeconds(5)));
        _states.ShouldBe([FormingState.Live]);
        _subscriptions.Count.ShouldBe(2);
    }

    // The last viewer left while the subscription was being made: it is taken off once it is made.
    [Test]
    public async Task Subscription_made_after_the_last_viewer_left_is_taken_off()
    {
        var subscribing = new TaskCompletionSource();
        _whileSubscribing = () => subscribing.Task;
        Task<LiveTradeFlowSnapshot> join = _feeds.JoinAsync("a", Sol, CancellationToken.None);

        await _feeds.LeaveAsync("a");
        subscribing.SetResult();
        await join;

        _subscriptions.Single()!.Disposed.ShouldBe(1);
    }

    private sealed class Subscription(Instrument instrument, Action<TradeEvent> traded, Action lost, Action closed) : IAsyncDisposable
    {
        public Instrument Instrument => instrument;

        public int Disposed { get; private set; }

        public Action<TradeEvent> Traded => traded;

        public Action Lost => lost;

        public Action Closed => closed;

        public ValueTask DisposeAsync()
        {
            Disposed++;

            return ValueTask.CompletedTask;
        }
    }
}