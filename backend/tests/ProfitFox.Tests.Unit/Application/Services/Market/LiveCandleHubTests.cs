using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Application.Services.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Unit.Application.Commands.Signals;
using ProfitFox.Tests.Unit.Application.Services.Ideas;

namespace ProfitFox.Tests.Unit.Application.Services.Market;

/// <summary>
///     Change add-ideas-and-market, task 11.5: spec market-overview «Живая свеча» — the candle of the timeframe from the
///     minutes of its window, no more than once a second, closed at the end of the window; one subscription of the feed
///     with the forming minutes, only while a page listens.
/// </summary>
public sealed class LiveCandleHubTests
{
    private static readonly DateTimeOffset Fourteen = new DateTimeOffset(2026, 11, 22, 14, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private FakeTimeProvider _time = null!;
    private Feed _feed = null!;
    private FakeRecentHistory _history = null!;
    private LiveCandleHub _hub = null!;

    [SetUp]
    public void Create()
    {
        _time = new FakeTimeProvider(Fourteen.AddMinutes(32).AddSeconds(5));
        _feed = new Feed();
        _history = new FakeRecentHistory();
        _history.Candles["sol"] = (interval, before) =>
            [.. Enumerable.Range(0, 32).Select(minute => new HistoryCandle(Fourteen.AddMinutes(minute), "100.5", "101", "100", "100.25", "12.5", Turnover: null, BuyVolume: null))];
        _hub = new LiveCandleHub(_feed, _history, new ListedPairs(), _time);
    }

    [Test]
    public async Task The_feed_is_subscribed_with_the_forming_minutes_only_while_pages_listen()
    {
        Assert.That(_feed.Active, Is.Zero, "no page, no subscription");
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        Task one = Drain(_hub.Listen("SOLUSDT", Hour, first.Token));
        Task two = Drain(_hub.Listen("ETHUSDT", TimeSpan.FromMinutes(5), second.Token));
        await Until(() => _feed.Active == 1 && _hub.Listeners == 2);

        Assert.That(_feed.Subscriptions.Single(), Is.EqualTo(new MarketSubscription(Since: null, Forming: true, MinutesOnly: true)), "one subscription for two pages");
        await first.CancelAsync();
        await one;
        Assert.That((_feed.Active, _hub.Listeners), Is.EqualTo((1, 1)));
        await second.CancelAsync();
        await two;
        await Until(() => _feed.Active == 0);
        Assert.That(_hub.Listeners, Is.Zero);
    }

    [Test]
    public async Task A_pair_outside_the_set_gives_nothing_and_subscribes_nothing()
    {
        List<LiveCandle> candles = [];
        await foreach (LiveCandle candle in _hub.Listen("NOSUCHUSDT", Hour, CancellationToken.None))
        {
            candles.Add(candle);
        }

        Assert.That((candles.Count, _feed.Subscriptions.Count), Is.EqualTo((0, 0)));
    }

    [Test]
    public async Task The_candle_of_the_hour_is_built_from_the_minutes_read_and_the_one_that_goes_on_no_more_than_once_a_second()
    {
        using var stop = new CancellationTokenSource();
        await using IAsyncEnumerator<LiveCandle> candles = _hub.Listen("SOLUSDT", Hour, stop.Token).GetAsyncEnumerator(stop.Token);
        await Seed(candles);
        await Until(() => _feed.Active == 1 && _hub.Listeners == 1);

        _feed.Push(Minute(32, high: 102, close: 101.5m));
        // The first minute of the feed goes at once, on the clock that stands: the seed is not counted in the second.
        await Seed(candles);
        LiveCandle first = candles.Current;
        _feed.Push(Minute(32, high: 102, close: 101.7m, low: 99.5m));
        Task<bool> next;
        next = candles.MoveNextAsync().AsTask();
        await Task.Delay(100);
        Assert.That(next.IsCompleted, Is.False, "a second change within the same second waits");
        LiveCandle second = await Advance(next, candles);

        Assert.That((first.OpenTime, first.Open, first.High, first.Low, first.Close, first.Closed), Is.EqualTo((Fourteen, 100.5m, 102m, 100m, 101.5m, false)));
        Assert.That(first.Volume, Is.EqualTo(33 * 12.5m), "32 minutes read and the one that goes on");
        Assert.That((second.Low, second.Close), Is.EqualTo((99.5m, 101.7m)), "the minute that goes on is replaced, not added");
        Assert.That(second.UpdatedAt - first.UpdatedAt, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(_history.Asked, Is.EqualTo(new[] { "candles sol" }), "the closed minutes of the window read once");
        await stop.CancelAsync();
    }

    [Test]
    public async Task At_the_end_of_the_window_the_candle_is_closed_and_a_new_one_begins()
    {
        using var stop = new CancellationTokenSource();
        await using IAsyncEnumerator<LiveCandle> candles = _hub.Listen("SOLUSDT", Hour, stop.Token).GetAsyncEnumerator(stop.Token);
        await Seed(candles);
        await Until(() => _feed.Active == 1);
        _feed.Push(Minute(32, high: 102, close: 101.5m));
        await Seed(candles);
        Assert.That(candles.Current.Close, Is.EqualTo(101.5m));

        _feed.Push(Minute(59, high: 103, close: 102.5m));
        _feed.Push(Minute(60, high: 101, close: 100.75m));
        Assert.That(await candles.MoveNextAsync(), Is.True);
        LiveCandle closed = candles.Current;
        LiveCandle fresh = await Advance(candles.MoveNextAsync().AsTask(), candles);

        Assert.That((closed.OpenTime, closed.High, closed.Close, closed.Closed), Is.EqualTo((Fourteen, 103m, 102.5m, true)), "the candle of 14:00 closed at 15:00");
        Assert.That((fresh.OpenTime, fresh.Open, fresh.Close, fresh.Closed), Is.EqualTo((Fourteen + Hour, 100.5m, 100.75m, false)));
        await stop.CancelAsync();
    }

    [Test]
    public async Task When_the_clock_passes_the_end_without_a_minute_the_candle_closes()
    {
        using var stop = new CancellationTokenSource();
        await using IAsyncEnumerator<LiveCandle> candles = _hub.Listen("SOLUSDT", Hour, stop.Token).GetAsyncEnumerator(stop.Token);
        Task<bool> next = candles.MoveNextAsync().AsTask();
        await Until(() => _feed.Active == 1);
        Assert.That(await next, Is.True, "the candle of the seed");
        _feed.Push(Minute(32, high: 102, close: 101.5m));
        await Seed(candles);
        Assert.That(candles.Current.Close, Is.EqualTo(101.5m));

        _time.SetUtcNow(Fourteen + Hour + TimeSpan.FromSeconds(1));
        LiveCandle closed = await Advance(candles.MoveNextAsync().AsTask(), candles);

        Assert.That((closed.OpenTime, closed.Close, closed.Closed), Is.EqualTo((Fourteen, 101.5m, true)));
        await stop.CancelAsync();
    }

    /// <summary>
    ///     Task 18.11 (а): a failure of the feed ends the subscription and comes to every page as an error; the next page
    ///     subscribes again.
    /// </summary>
    [Test]
    public async Task A_failure_of_the_feed_comes_to_the_pages_as_an_error_and_the_next_page_subscribes_again()
    {
        using var stop = new CancellationTokenSource();
        await using IAsyncEnumerator<LiveCandle> candles = _hub.Listen("SOLUSDT", Hour, stop.Token).GetAsyncEnumerator(stop.Token);
        await Seed(candles);
        Task<bool> next = candles.MoveNextAsync().AsTask();
        await Until(() => _feed.Active == 1 && _hub.Listeners == 1);

        _feed.Fail(new InvalidOperationException("the catalog did not answer"));

        Exception? failure = Assert.CatchAsync(async () => await next);
        Assert.That(failure!.Message, Does.Contain("the catalog did not answer"));
        await Until(() => _hub.Listeners == 0);
        using var again = new CancellationTokenSource();
        Task listening = Drain(_hub.Listen("SOLUSDT", Hour, again.Token));
        await Until(() => _feed.Active == 1);
        Assert.That(_feed.Subscriptions, Has.Count.EqualTo(2), "a new subscription for the next page");
        await again.CancelAsync();
        await listening;
    }

    /// <summary>
    ///     Task 19.5: a quiet pair — the feed tells no minute — gets the candle of the window from the seed right after it
    ///     subscribed, without waiting for a minute or a second of the clock.
    /// </summary>
    [Test]
    public async Task A_quiet_pair_gets_the_candle_of_the_seed_right_away()
    {
        using var stop = new CancellationTokenSource();
        await using IAsyncEnumerator<LiveCandle> candles = _hub.Listen("SOLUSDT", Hour, stop.Token).GetAsyncEnumerator(stop.Token);

        Task<bool> next = candles.MoveNextAsync().AsTask();
        Assert.That(await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(5))), Is.SameAs(next), "no minute of the feed, the clock stands");

        LiveCandle seed = candles.Current;
        Assert.That((seed.OpenTime, seed.Open, seed.High, seed.Low, seed.Close, seed.Volume, seed.Closed), Is.EqualTo((Fourteen, 100.5m, 101m, 100m, 100.25m, 32 * 12.5m, false)));
        await stop.CancelAsync();
    }

    /// <summary>
    ///     Task 19.5: a window without a trade yet — the hour just began — gives a flat candle at the last close before it.
    /// </summary>
    [Test]
    public async Task A_window_without_a_trade_gives_a_flat_candle_at_the_last_close()
    {
        _history.Candles["sol"] = (interval, before) =>
            [new HistoryCandle(Fourteen.AddMinutes(-1), "100.5", "101", "100", "100.25", "12.5", Turnover: null, BuyVolume: null)];
        var hub = new LiveCandleHub(_feed, _history, new ListedPairs(), new FakeTimeProvider(Fourteen.AddSeconds(20)));
        using var stop = new CancellationTokenSource();
        await using IAsyncEnumerator<LiveCandle> candles = hub.Listen("SOLUSDT", TimeSpan.FromMinutes(5), stop.Token).GetAsyncEnumerator(stop.Token);

        Task<bool> next = candles.MoveNextAsync().AsTask();
        Assert.That(await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(5))), Is.SameAs(next));

        LiveCandle flat = candles.Current;
        Assert.That((flat.OpenTime, flat.Open, flat.High, flat.Low, flat.Close, flat.Volume, flat.Closed), Is.EqualTo((Fourteen, 100.25m, 100.25m, 100.25m, 100.25m, 0m, false)));
        await stop.CancelAsync();
    }

    /// <summary>
    ///     Task 19.5: a former subscription that ends after a new one began — its stream of gRPC ended a moment after the last
    ///     page left — touches neither the pages of the new one nor the new subscription.
    /// </summary>
    [Test]
    public async Task A_former_subscription_that_fails_late_leaves_the_new_one_alone()
    {
        _feed.Lingers = true;
        using var first = new CancellationTokenSource();
        Task one = Drain(_hub.Listen("SOLUSDT", Hour, first.Token));
        await Until(() => _feed.Active == 1 && _hub.Listeners == 1);
        await first.CancelAsync();
        await one;
        _feed.Lingers = false;

        using var stop = new CancellationTokenSource();
        await using IAsyncEnumerator<LiveCandle> candles = _hub.Listen("SOLUSDT", Hour, stop.Token).GetAsyncEnumerator(stop.Token);
        await Seed(candles);
        await Until(() => _feed.Active == 2);
        _feed.FailOldest(new InvalidOperationException("the former stream broke"));
        await Until(() => _feed.Active == 1);

        _feed.Push(Minute(32, high: 102, close: 101.5m));
        LiveCandle live = await Advance(candles.MoveNextAsync().AsTask(), candles);
        Assert.That(live.Close, Is.EqualTo(101.5m), "the page of the new subscription goes on");
        await candles.DisposeAsync();
        await Until(() => _feed.Active == 0);
    }

    /// <summary>
    ///     Task 18.11 (б): the hub tells whether a pair is in the set, so the page of a pair outside it gets 404 before any
    ///     stream.
    /// </summary>
    [Test]
    public async Task The_hub_tells_whether_a_pair_is_in_the_set()
    {
        Assert.That((await _hub.IsListed("SOLUSDT", CancellationToken.None), await _hub.IsListed("NOSUCHUSDT", CancellationToken.None)), Is.EqualTo((true, false)));
    }

    /// <summary>
    ///     Task 18.11 (в): after 16:40 a day has more than 1000 minutes, and the candle of the day is built from all of them,
    ///     read in two windows.
    /// </summary>
    [Test]
    public async Task The_candle_of_the_day_is_built_from_every_minute_of_the_day()
    {
        DateTimeOffset midnight = Fourteen.AddHours(-14);
        _time.SetUtcNow(midnight.AddHours(18).AddSeconds(5));
        _history.Candles["sol"] = (interval, before) =>
            [.. Enumerable.Range(0, 18 * 60).Select(minute => new HistoryCandle(midnight.AddMinutes(minute), minute == 0 ? "50" : "100.5", minute == 1 ? "200" : "101", "100", "100.25", "12.5", Turnover: null, BuyVolume: null))];
        using var stop = new CancellationTokenSource();
        await using IAsyncEnumerator<LiveCandle> candles = _hub.Listen("SOLUSDT", TimeSpan.FromDays(1), stop.Token).GetAsyncEnumerator(stop.Token);
        await Seed(candles);

        LiveCandle day = candles.Current;
        Assert.That((day.OpenTime, day.Open, day.High), Is.EqualTo((midnight, 50m, 200m)), "the minutes of 00:00 and 00:01 are in");
        Assert.That(day.Volume, Is.EqualTo(18 * 60 * 12.5m));
        Assert.That(_history.Asked, Is.EqualTo(new[] { "candles sol", "candles sol" }), "two windows of at most 1000 minutes");
        await stop.CancelAsync();
    }

    // The next candle comes on a clock that stands (task 19.5): the candle of the seed right after the page subscribed, and
    // the first minute of the feed after it.
    private static async Task Seed(IAsyncEnumerator<LiveCandle> candles)
    {
        Task<bool> next = candles.MoveNextAsync().AsTask();
        Assert.That(await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(5))), Is.SameAs(next), "the candle comes without the clock moving");
        Assert.That(await next, Is.True);
    }

    // The clock moves a second at a time until the candle comes: the hub waits for its second on the clock it was given.
    private async Task<LiveCandle> Advance(Task<bool> next, IAsyncEnumerator<LiveCandle> candles)
    {
        for (int step = 0; step < 100 && !next.IsCompleted; step++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.WhenAny(next, Task.Delay(20));
        }

        Assert.That(await next, Is.True);
        return candles.Current;
    }

    private static LiveMinute Minute(int minute, decimal high, decimal close, decimal low = 100m)
    {
        return new LiveMinute("SOLUSDT", Fourteen.AddMinutes(minute), 100.5m, high, low, close, 12.5m, 12.5m * close, Closed: false);
    }

    private static async Task Drain(IAsyncEnumerable<LiveCandle> candles)
    {
        await foreach (LiveCandle _ in candles)
        {
        }
    }

    private static async Task Until(Func<bool> condition, [CallerArgumentExpression(nameof(condition))] string? what = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (timeout.IsCancellationRequested)
            {
                Assert.Fail($"Not in time: {what}");
            }

            await Task.Delay(10, CancellationToken.None);
        }
    }

    // The live feed by hand: the subscriptions it was asked for, how many go on now, and the minutes a test pushes.
    private sealed class Feed : IMarketFeed
    {
        private readonly Lock _lock = new Lock();
        private readonly List<Channel<MarketEvent>> _readers = [];

        public List<MarketSubscription> Subscriptions { get; } = [];

        /// <summary>
        ///     Whether a new subscription goes on after its cancellation until it is failed: a stream of gRPC that ends a moment
        ///     after the last page left (task 19.5).
        /// </summary>
        public bool Lingers { get; set; }

        /// <summary>
        ///     Ends the oldest subscription with <paramref name="exception" />.
        /// </summary>
        public void FailOldest(Exception exception)
        {
            lock (_lock)
            {
                _readers[0].Writer.TryComplete(exception);
            }
        }

        public int Active
        {
            get
            {
                lock (_lock)
                {
                    return _readers.Count;
                }
            }
        }

        public void Fail(Exception exception)
        {
            lock (_lock)
            {
                foreach (Channel<MarketEvent> reader in _readers)
                {
                    reader.Writer.TryComplete(exception);
                }
            }
        }

        public void Push(MarketEvent item)
        {
            lock (_lock)
            {
                foreach (Channel<MarketEvent> reader in _readers)
                {
                    reader.Writer.TryWrite(item);
                }
            }
        }

        public async IAsyncEnumerable<MarketEvent> Subscribe(MarketSubscription subscription, [EnumeratorCancellation] CancellationToken cancellation)
        {
            var channel = Channel.CreateUnbounded<MarketEvent>();
            lock (_lock)
            {
                Subscriptions.Add(subscription);
                _readers.Add(channel);
            }

            CancellationToken reading = Lingers ? CancellationToken.None : cancellation;
            try
            {
                await foreach (MarketEvent item in channel.Reader.ReadAllAsync(reading))
                {
                    yield return item;
                }
            }
            finally
            {
                lock (_lock)
                {
                    _readers.Remove(channel);
                }
            }
        }
    }
}
