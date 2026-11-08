using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;

namespace ProfitFox.Application.MarketData.FormingCandles;

/// <summary>
///     The feeds of the current candle, one per series however many viewers watch it: the first viewer subscribes the port, the
///     last to leave takes the subscription off. A connection follows one series at a time. Only the API registers it. The port is the
///     router of the exchange of the category, so a series of MOEX comes from the streams of T-Invest.
/// </summary>
public sealed class FormingCandleFeeds(IFormingCandleStreams streams, IFormingCandleBroadcast broadcast, TimeProvider time)
{
    private readonly Lock _lock = new Lock();
    private readonly Dictionary<FormingSeries, Feed> _feeds = [];
    private readonly Dictionary<string, FormingSeries> _viewing = [];

    /// <summary>Makes the connection a viewer of the series, after it leaves the series it watched; returns where the feed is now.</summary>
    public async Task<FormingSnapshot> JoinAsync(string connectionId, FormingSeries series, CancellationToken cancellationToken)
    {
        Feed? leaving = null;
        Feed feed;
        bool created = false;

        lock (_lock)
        {
            if (_viewing.TryGetValue(connectionId, out FormingSeries? current))
            {
                if (current == series)
                {
                    return _feeds[series].Snapshot();
                }

                leaving = Detach(connectionId, current);
            }

            if (!_feeds.TryGetValue(series, out feed!))
            {
                feed = _feeds[series] = new Feed(series, streams, broadcast, time);
                created = true;
            }

            feed.Viewers.Add(connectionId);
            _viewing[connectionId] = series;
        }

        if (leaving is not null)
        {
            await leaving.CloseAsync();
        }

        if (created)
        {
            feed.Start();
        }

        return feed.Snapshot();
    }

    public async Task LeaveAsync(string connectionId)
    {
        Feed? leaving = null;

        lock (_lock)
        {
            if (_viewing.TryGetValue(connectionId, out FormingSeries? current))
            {
                leaving = Detach(connectionId, current);
            }
        }

        if (leaving is not null)
        {
            await leaving.CloseAsync();
        }
    }

    /// <summary>Removes the viewer; returns the feed when nobody watches it any more.</summary>
    private Feed? Detach(string connectionId, FormingSeries series)
    {
        _viewing.Remove(connectionId);
        Feed feed = _feeds[series];
        feed.Viewers.Remove(connectionId);

        if (feed.Viewers.Count > 0)
        {
            return null;
        }

        _feeds.Remove(series);

        return feed;
    }

    private sealed class Feed
    {
        private static readonly TimeSpan Silence = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan LastRetry = TimeSpan.FromMinutes(1);

        private readonly FormingSeries _series;
        private readonly IFormingCandleStreams _streams;
        private readonly IFormingCandleBroadcast _broadcast;
        private readonly TimeProvider _time;
        private readonly FormingCandleThrottle _throttle;
        private readonly Lock _lock = new Lock();
        private FormingState _state = FormingState.Connecting;
        private IAsyncDisposable? _subscription;
        private ITimer? _silence;
        private ITimer? _retry;
        private TimeSpan _retryDelay = FirstRetry;

        // The attempt whose events count; the callbacks of an earlier subscription find it replaced and say nothing.
        private object? _attempt;

        // Set for good once the feed is closed: a start that comes after it must not subscribe.
        private bool _closed;

        public Feed(FormingSeries series, IFormingCandleStreams streams, IFormingCandleBroadcast broadcast, TimeProvider time)
        {
            _series = series;
            _streams = streams;
            _broadcast = broadcast;
            _time = time;
            _throttle = new FormingCandleThrottle(time, candle => Observe(broadcast.CandleAsync(series, candle)));
        }

        /// <summary>The connections watching the series; guarded by the lock of <see cref="FormingCandleFeeds" />, not by the feed's.</summary>
        public HashSet<string> Viewers { get; } = [];

        public void Start()
        {
            _ = ConnectAsync();
        }

        public FormingSnapshot Snapshot()
        {
            lock (_lock)
            {
                return new FormingSnapshot(_state, _throttle.Latest);
            }
        }

        public async Task CloseAsync()
        {
            IAsyncDisposable? subscription;

            lock (_lock)
            {
                _closed = true;
                _attempt = null;
                subscription = _subscription;
                _subscription = null;
                _silence?.Dispose();
                _retry?.Dispose();
                _throttle.Dispose();
            }

            if (subscription is not null)
            {
                await subscription.DisposeAsync();
            }
        }

        private static void Observe(Task send)
        {
            // A failed send is not the feed's: the next update or the snapshot of a new viewer tells the viewers again.
            send.ContinueWith(static task => task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        private async Task ConnectAsync()
        {
            var attempt = new object();
            IAsyncDisposable subscription;

            lock (_lock)
            {
                if (_closed)
                {
                    return;
                }

                _attempt = attempt;
            }

            try
            {
                subscription = await _streams.SubscribeAsync(
                    _series.Instrument,
                    _series.Interval,
                    candle => OnUpdate(attempt, candle),
                    () => OnLost(attempt),
                    static () => { },
                    () => OnClosed(attempt),
                    CancellationToken.None);
            }
            catch (ExchangeException)
            {
                lock (_lock)
                {
                    if (_attempt == attempt)
                    {
                        SetState(FormingState.NoData);
                        ScheduleRetry();
                    }
                }

                return;
            }

            bool discard;

            lock (_lock)
            {
                // Closed for good, or the whole feed closed, while it was being made.
                discard = _attempt != attempt;

                if (!discard)
                {
                    _subscription = subscription;
                    _retryDelay = FirstRetry;

                    if (_state == FormingState.NoData)
                    {
                        SetState(FormingState.Connecting);
                    }

                    ArmSilence();
                }
            }

            if (discard)
            {
                await subscription.DisposeAsync();
            }
        }

        private void OnUpdate(object attempt, FormingCandle candle)
        {
            lock (_lock)
            {
                if (_attempt != attempt)
                {
                    return;
                }

                SetState(FormingState.Live);
                ArmSilence();
                _throttle.Offer(candle);
            }
        }

        // A restored connection says nothing here: the feed stays without data until the next update.
        private void OnLost(object attempt)
        {
            lock (_lock)
            {
                if (_attempt == attempt)
                {
                    SetState(FormingState.NoData);
                }
            }
        }

        private void OnClosed(object attempt)
        {
            IAsyncDisposable? replaced;

            lock (_lock)
            {
                if (_attempt != attempt)
                {
                    return;
                }

                _attempt = null;
                replaced = _subscription;
                _subscription = null;
                SetState(FormingState.NoData);
                ScheduleRetry();
            }

            _ = replaced?.DisposeAsync().AsTask();
        }

        private void ArmSilence()
        {
            if (_silence is null)
            {
                _silence = _time.CreateTimer(_ => OnSilence(), null, Silence, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _silence.Change(Silence, Timeout.InfiniteTimeSpan);
            }
        }

        private void OnSilence()
        {
            lock (_lock)
            {
                SetState(FormingState.NoData);
            }
        }

        private void ScheduleRetry()
        {
            _retry?.Dispose();
            _retry = _time.CreateTimer(_ => Start(), null, _retryDelay, Timeout.InfiniteTimeSpan);
            _retryDelay = TimeSpan.FromTicks(Math.Min(_retryDelay.Ticks * 2, LastRetry.Ticks));
        }

        private void SetState(FormingState state)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            Observe(_broadcast.StatusAsync(_series, state));
        }
    }
}