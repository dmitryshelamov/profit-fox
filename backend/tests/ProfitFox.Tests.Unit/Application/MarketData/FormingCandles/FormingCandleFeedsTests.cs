using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.MarketData.FormingCandles;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.MarketData.FormingCandles;

/// <summary>Requirements "Одна подписка на серию" and "Состояние канала текущей свечи".</summary>
public class FormingCandleFeedsTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-10T12:30:00Z");
    private static readonly DateTimeOffset Hour = DateTimeOffset.Parse("2024-03-10T12:00:00Z");
    private static readonly FormingSeries Btc = new(new Instrument(MarketCategory.Spot, "BTCUSDT"), Interval.OneHour);
    private static readonly FormingSeries Eth = new(new Instrument(MarketCategory.Spot, "ETHUSDT"), Interval.OneHour);
    private static readonly FormingSeries Sber = new(new Instrument(MarketCategory.Moex, "SBER"), Interval.OneHour);

    private FakeTimeProvider _time = null!;
    private IFormingCandleStreams _streams = null!;
    private IFormingCandleBroadcast _broadcast = null!;
    private List<Subscription> _subscriptions = null!;
    private int _failures;
    private Action<Subscription>? _whileSubscribing;
    private FormingCandleFeeds _feeds = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Start);
        _subscriptions = [];
        _failures = 0;
        _whileSubscribing = null;
        _broadcast = Substitute.For<IFormingCandleBroadcast>();
        _broadcast.CandleAsync(default!, default!).ReturnsForAnyArgs(Task.CompletedTask);
        _broadcast.StatusAsync(default!, default).ReturnsForAnyArgs(Task.CompletedTask);
        _streams = Substitute.For<IFormingCandleStreams>();
        _streams.SubscribeAsync(default!, default, default!, default!, default!, default!, default).ReturnsForAnyArgs(call =>
        {
            if (_failures > 0)
            {
                _failures--;
                _subscriptions.Add(null!);

                return Task.FromException<IAsyncDisposable>(new ExchangeUnavailableException("down"));
            }

            var subscription = new Subscription(call.ArgAt<Instrument>(0), call.ArgAt<Action<FormingCandle>>(2), call.ArgAt<Action>(3), call.ArgAt<Action>(4), call.ArgAt<Action>(5));
            _subscriptions.Add(subscription);
            _whileSubscribing?.Invoke(subscription);

            return Task.FromResult<IAsyncDisposable>(subscription);
        });
        _feeds = new FormingCandleFeeds(_streams, _broadcast, _time);
    }

    // After the review: T-Invest has no stream of the forming candle until the live bots of MOEX come, so a series of moex is without data at
    // once, and nothing is asked of the streams of Bybit, which would refuse the symbol and be asked again and again.
    [Test]
    public async Task A_series_of_moex_is_without_data_and_subscribes_nothing()
    {
        (await _feeds.JoinAsync("a", Sber, CancellationToken.None)).ShouldBe(new FormingSnapshot(FormingState.NoData, null));

        _time.Advance(TimeSpan.FromMinutes(5));
        _streams.ReceivedCalls().ShouldBeEmpty();
        _broadcast.ReceivedCalls().ShouldBeEmpty();
        await _feeds.LeaveAsync("a");
    }

    [Test]
    public async Task A_viewer_that_moves_to_a_series_of_moex_leaves_its_feed()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);

        await _feeds.JoinAsync("a", Sber, CancellationToken.None);

        _subscriptions.Single().Disposed.ShouldBe(1);
    }

    // Scenario "Две вкладки" / "Последняя вкладка закрыта".
    [Test]
    public async Task The_first_viewer_subscribes_the_port_and_the_second_joins_it()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        await _feeds.JoinAsync("b", Btc, CancellationToken.None);

        _subscriptions.Count.ShouldBe(1);
        _subscriptions[0].Instrument.ShouldBe(Btc.Instrument);
        await _streams.Received(1).SubscribeAsync(Btc.Instrument, Btc.Interval, Arg.Any<Action<FormingCandle>>(), Arg.Any<Action>(), Arg.Any<Action>(), Arg.Any<Action>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task The_last_viewer_to_leave_takes_the_subscription_off()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        await _feeds.JoinAsync("b", Btc, CancellationToken.None);

        await _feeds.LeaveAsync("a");
        _subscriptions[0].Disposed.ShouldBe(0);
        await _feeds.LeaveAsync("b");

        _subscriptions[0].Disposed.ShouldBe(1);
    }

    [Test]
    public async Task Leaving_without_having_joined_is_harmless()
    {
        await _feeds.LeaveAsync("nobody");

        _subscriptions.ShouldBeEmpty();
    }

    [Test]
    public async Task A_new_join_of_the_same_connection_leaves_its_previous_feed()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);

        await _feeds.JoinAsync("a", Eth, CancellationToken.None);

        _subscriptions.Select(subscription => (subscription.Instrument.Symbol, subscription.Disposed)).ShouldBe([("BTCUSDT", 1), ("ETHUSDT", 0)]);
    }

    [Test]
    public async Task Joining_the_same_series_again_keeps_one_viewer_and_one_subscription()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);

        _subscriptions.Count.ShouldBe(1);
        _subscriptions[0].Disposed.ShouldBe(0);
        await _feeds.LeaveAsync("a");
        _subscriptions[0].Disposed.ShouldBe(1);
    }

    [Test]
    public async Task A_series_comes_back_with_a_new_subscription_after_its_last_viewer_left()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        await _feeds.LeaveAsync("a");

        await _feeds.JoinAsync("b", Btc, CancellationToken.None);

        _subscriptions.Count.ShouldBe(2);
    }

    [Test]
    public async Task A_viewer_that_leaves_before_the_subscription_is_made_leaves_it_off()
    {
        var gate = new TaskCompletionSource<IAsyncDisposable>();
        var subscription = new Subscription(Btc.Instrument, _ => { }, () => { }, () => { }, () => { });
        _streams.SubscribeAsync(default!, default, default!, default!, default!, default!, default).ReturnsForAnyArgs(gate.Task);
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        await _feeds.LeaveAsync("a");

        gate.SetResult(subscription);
        await Task.Delay(50);

        subscription.Disposed.ShouldBe(1);
    }

    // Scenario "Связь с Bybit оборвалась", "Связь восстановилась", "Тишина".
    [Test]
    public async Task The_snapshot_is_connecting_until_the_first_update_then_live_with_the_candle()
    {
        (await _feeds.JoinAsync("a", Btc, CancellationToken.None)).ShouldBe(new FormingSnapshot(FormingState.Connecting, null));

        FormingCandle candle = Update(100m, seconds: 1);
        _subscriptions[0].Update(candle);

        (await _feeds.JoinAsync("b", Btc, CancellationToken.None)).ShouldBe(new FormingSnapshot(FormingState.Live, candle));
    }

    [Test]
    public async Task The_first_update_is_broadcast_with_the_live_status()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        FormingCandle candle = Update(100m, seconds: 1);

        _subscriptions[0].Update(candle);

        await _broadcast.Received(1).StatusAsync(Btc, FormingState.Live);
        await _broadcast.Received(1).CandleAsync(Btc, candle);
    }

    [Test]
    public async Task Lost_connection_means_no_data_and_restored_waits_for_the_next_update()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        _subscriptions[0].Update(Update(100m, seconds: 1));

        _subscriptions[0].Lost();
        (await StateAsync()).ShouldBe(FormingState.NoData);
        _subscriptions[0].Restored();
        (await StateAsync()).ShouldBe(FormingState.NoData);
        _subscriptions[0].Update(Update(101m, seconds: 2));

        (await StateAsync()).ShouldBe(FormingState.Live);
        Received.InOrder(() =>
        {
            _broadcast.StatusAsync(Btc, FormingState.Live);
            _broadcast.StatusAsync(Btc, FormingState.NoData);
            _broadcast.StatusAsync(Btc, FormingState.Live);
        });
    }

    [Test]
    public async Task Ninety_seconds_without_updates_mean_no_data_and_every_update_restarts_the_wait()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        _subscriptions[0].Update(Update(100m, seconds: 1));
        _time.Advance(TimeSpan.FromSeconds(60));
        _subscriptions[0].Update(Update(101m, seconds: 61));
        _time.Advance(TimeSpan.FromSeconds(60));
        (await StateAsync()).ShouldBe(FormingState.Live);

        _time.Advance(TimeSpan.FromSeconds(30));

        (await StateAsync()).ShouldBe(FormingState.NoData);
    }

    [Test]
    public async Task Ninety_seconds_without_a_first_update_mean_no_data()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(89));
        (await StateAsync()).ShouldBe(FormingState.Connecting);

        _time.Advance(TimeSpan.FromSeconds(1));

        (await StateAsync()).ShouldBe(FormingState.NoData);
    }

    // Retries of a refused or closed subscription: 1 s, doubling up to a minute, only while someone watches.
    [Test]
    public async Task A_refused_subscription_is_tried_again_after_one_second_doubling_to_a_minute()
    {
        _failures = 100;

        (await _feeds.JoinAsync("a", Btc, CancellationToken.None)).State.ShouldBe(FormingState.NoData);
        _subscriptions.Count.ShouldBe(1);

        var attempts = new List<TimeSpan>();
        TimeSpan elapsed = TimeSpan.Zero;

        while (attempts.Count < 8)
        {
            int before = _subscriptions.Count;
            _time.Advance(TimeSpan.FromSeconds(1));
            elapsed += TimeSpan.FromSeconds(1);

            if (_subscriptions.Count > before)
            {
                attempts.Add(elapsed);
            }
        }

        // 1, then +2, +4, +8, +16, +32, +60, +60 seconds.
        attempts.Select(at => (int)at.TotalSeconds).ShouldBe([1, 3, 7, 15, 31, 63, 123, 183]);
    }

    [Test]
    public async Task A_retry_that_succeeds_brings_the_feed_up_and_resets_the_pause()
    {
        _failures = 2;
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(1));
        _time.Advance(TimeSpan.FromSeconds(2));

        _subscriptions.Count.ShouldBe(3);
        (await StateAsync()).ShouldBe(FormingState.Connecting);
        _subscriptions[2].Update(Update(100m, seconds: 1));
        (await StateAsync()).ShouldBe(FormingState.Live);

        _subscriptions[2].Closed();
        _time.Advance(TimeSpan.FromSeconds(1));

        _subscriptions.Count.ShouldBe(4);
    }

    [Test]
    public async Task A_subscription_closed_for_good_is_replaced_after_one_second()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        _subscriptions[0].Update(Update(100m, seconds: 1));

        _subscriptions[0].Closed();

        (await StateAsync()).ShouldBe(FormingState.NoData);
        _subscriptions[0].Disposed.ShouldBe(1);
        _subscriptions.Count.ShouldBe(1);
        _time.Advance(TimeSpan.FromSeconds(1));
        _subscriptions.Count.ShouldBe(2);
    }

    [Test]
    public async Task Without_viewers_nothing_is_tried_again()
    {
        _failures = 100;
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        await _feeds.LeaveAsync("a");

        _time.Advance(TimeSpan.FromMinutes(5));

        _subscriptions.Count.ShouldBe(1);
    }

    [Test]
    public async Task A_subscription_closed_while_it_is_being_made_is_dropped_and_made_again()
    {
        _whileSubscribing = subscription =>
        {
            if (_subscriptions.Count == 1)
            {
                subscription.Closed();
            }
        };

        (await _feeds.JoinAsync("a", Btc, CancellationToken.None)).State.ShouldBe(FormingState.NoData);

        _subscriptions[0].Disposed.ShouldBe(1);
        _time.Advance(TimeSpan.FromSeconds(1));
        _subscriptions.Count.ShouldBe(2);
        _subscriptions[0].Disposed.ShouldBe(1);
        _subscriptions[1].Disposed.ShouldBe(0);
    }

    [Test]
    public async Task An_update_that_comes_while_the_subscription_is_being_made_keeps_the_feed_live()
    {
        FormingCandle candle = Update(100m, seconds: 1);
        _whileSubscribing = subscription => subscription.Update(candle);

        (await _feeds.JoinAsync("a", Btc, CancellationToken.None)).ShouldBe(new FormingSnapshot(FormingState.Live, candle));
    }

    [Test]
    public async Task An_update_of_a_replaced_subscription_is_ignored()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        _subscriptions[0].Closed();
        _time.Advance(TimeSpan.FromSeconds(1));

        _subscriptions[0].Update(Update(100m, seconds: 1));

        (await StateAsync()).ShouldBe(FormingState.Connecting);
    }

    [Test]
    public async Task A_lost_connection_of_a_replaced_subscription_is_ignored()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        _subscriptions[0].Closed();
        _time.Advance(TimeSpan.FromSeconds(1));
        _subscriptions[1].Update(Update(100m, seconds: 1));

        _subscriptions[0].Lost();

        (await StateAsync()).ShouldBe(FormingState.Live);
    }

    // The connection leaves while its join is still closing the feed it watched before: the new feed is closed before it starts.
    [Test]
    public async Task A_feed_closed_before_it_starts_makes_no_subscription()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        var gate = new TaskCompletionSource();
        _subscriptions[0].DisposeGate = gate;
        Task joining = _feeds.JoinAsync("a", Eth, CancellationToken.None);

        await _feeds.LeaveAsync("a");
        gate.SetResult();
        await joining;
        await Task.Delay(50);

        _subscriptions.Select(subscription => subscription.Instrument.Symbol).ShouldBe(["BTCUSDT"]);
    }

    [Test]
    public async Task Events_after_the_last_viewer_left_change_nothing()
    {
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        await _feeds.LeaveAsync("a");

        _subscriptions[0].Update(Update(100m, seconds: 1));
        _subscriptions[0].Closed();
        _time.Advance(TimeSpan.FromMinutes(5));

        await _broadcast.DidNotReceiveWithAnyArgs().CandleAsync(default!, default!);
        _subscriptions.Count.ShouldBe(1);
    }

    [Test]
    public async Task A_refusal_that_comes_after_the_last_viewer_left_is_not_tried_again()
    {
        var gate = new TaskCompletionSource<IAsyncDisposable>();
        _streams.SubscribeAsync(default!, default, default!, default!, default!, default!, default).ReturnsForAnyArgs(gate.Task);
        await _feeds.JoinAsync("a", Btc, CancellationToken.None);
        await _feeds.LeaveAsync("a");

        gate.SetException(new ExchangeUnavailableException("down"));
        await Task.Delay(50);
        _time.Advance(TimeSpan.FromMinutes(5));

        await _streams.ReceivedWithAnyArgs(1).SubscribeAsync(default!, default, default!, default!, default!, default!, default);
        await _broadcast.DidNotReceiveWithAnyArgs().StatusAsync(default!, default);
    }

    private async Task<FormingState> StateAsync()
    {
        FormingSnapshot snapshot = await _feeds.JoinAsync("probe", Btc, CancellationToken.None);
        await _feeds.LeaveAsync("probe");

        return snapshot.State;
    }

    private static FormingCandle Update(decimal close, int seconds, bool closed = false)
    {
        return new FormingCandle(Hour, new Price(close), new Price(close), new Price(close), new Price(close), new Volume(1m), closed, Hour.AddMinutes(30).AddSeconds(seconds));
    }

    private sealed class Subscription(Instrument instrument, Action<FormingCandle> update, Action lost, Action restored, Action closed) : IAsyncDisposable
    {
        public Instrument Instrument => instrument;

        public int Disposed { get; private set; }

        public Action<FormingCandle> Update => update;

        public Action Lost => lost;

        public Action Restored => restored;

        public Action Closed => closed;

        /// <summary>When set, disposing waits for it.</summary>
        public TaskCompletionSource? DisposeGate { get; set; }

        public async ValueTask DisposeAsync()
        {
            Disposed++;

            if (DisposeGate is { } gate)
            {
                await gate.Task;
            }
        }
    }
}