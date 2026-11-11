using ProfitFox.Application.Exceptions;
using ProfitFox.Application.MarketData.FormingCandles;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.TradeFlow.Live;

/// <summary>
///     The feeds of the current bar of the trade flow (decision 10 of collect-trade-flow), one per pair however many viewers watch it, like
///     <see cref="FormingCandleFeeds" />: the first viewer subscribes the trades of the pair, the last to leave takes the subscription off, and a
///     connection follows one pair at a time. A feed adds up the trades of the current minute since it subscribed and starts again at the
///     boundary of a minute; the sums go out at most once a second, a new minute at once. Its state is <c>Connecting</c>, <c>Live</c> or
///     <c>NoData</c> (a lost connection, a refused or closed subscription, made again after <see cref="Retry" />, or 90 s of silence). Only the
///     API registers it.
/// </summary>
public sealed class TradeFlowFeeds(ITradeFlowStreams streams, ITradeFlowBroadcast broadcast, TimeProvider time)
{
    /// <summary>How long a feed waits before it makes a refused or closed subscription again.</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    private readonly Lock _lock = new Lock();
    private readonly Dictionary<Instrument, Feed> _feeds = [];
    private readonly Dictionary<string, Instrument> _viewing = [];

    /// <summary>Makes the connection a viewer of the pair, after it leaves the pair it watched; returns where the feed is now.</summary>
    public async Task<LiveTradeFlowSnapshot> JoinAsync(string connectionId, Instrument instrument, CancellationToken cancellationToken)
    {
        Feed? leaving = null;
        Feed feed;
        var created = false;

        lock (_lock)
        {
            if (_viewing.TryGetValue(connectionId, out Instrument? current))
            {
                if (current == instrument)
                {
                    return _feeds[instrument].Snapshot();
                }

                leaving = Detach(connectionId, current);
            }

            if (!_feeds.TryGetValue(instrument, out feed!))
            {
                feed = _feeds[instrument] = new Feed(instrument, streams, broadcast, time);
                created = true;
            }

            feed.Viewers.Add(connectionId);
            _viewing[connectionId] = instrument;
        }

        if (leaving is not null)
        {
            await leaving.CloseAsync();
        }

        if (created)
        {
            await feed.ConnectAsync();
        }

        return feed.Snapshot();
    }

    public async Task LeaveAsync(string connectionId)
    {
        Feed? leaving = null;

        lock (_lock)
        {
            if (_viewing.TryGetValue(connectionId, out Instrument? current))
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
    private Feed? Detach(string connectionId, Instrument instrument)
    {
        _viewing.Remove(connectionId);
        Feed feed = _feeds[instrument];
        feed.Viewers.Remove(connectionId);

        if (feed.Viewers.Count > 0)
        {
            return null;
        }

        _feeds.Remove(instrument);

        return feed;
    }

    private sealed class Feed(Instrument instrument, ITradeFlowStreams streams, ITradeFlowBroadcast broadcast, TimeProvider time)
    {
        private static readonly TimeSpan Silence = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan Period = TimeSpan.FromSeconds(1);

        private readonly Lock _lock = new Lock();
        private FormingState _state = FormingState.Connecting;
        private IAsyncDisposable? _subscription;
        private ITimer? _silence;
        private ITimer? _retry;
        private ITimer? _held;
        private LiveTradeFlow? _latest;
        private DateTimeOffset _sentAt;
        private DateTimeOffset _since;

        // The attempt whose events count; the callbacks of an earlier subscription find it replaced and say nothing.
        private object? _attempt;

        // Set for good once the feed is closed: a retry that comes after it must not subscribe.
        private bool _closed;

        /// <summary>The connections watching the pair; guarded by the lock of <see cref="TradeFlowFeeds" />, not by the feed's.</summary>
        public HashSet<string> Viewers { get; } = [];

        public LiveTradeFlowSnapshot Snapshot()
        {
            lock (_lock)
            {
                return new LiveTradeFlowSnapshot(_state, _latest);
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
                _held?.Dispose();
            }

            if (subscription is not null)
            {
                await subscription.DisposeAsync();
            }
        }

        public async Task ConnectAsync()
        {
            var attempt = new object();

            lock (_lock)
            {
                if (_closed)
                {
                    return;
                }

                _attempt = attempt;
                _since = time.GetUtcNow();
                _latest = null;
            }

            IAsyncDisposable subscription;

            try
            {
                subscription = await streams.SubscribeTradesAsync(instrument, trade => OnTrade(attempt, trade), () => OnLost(attempt), static () => { }, () => OnClosed(attempt), CancellationToken.None);
            }
            catch (ExchangeException)
            {
                lock (_lock)
                {
                    SetState(FormingState.NoData);
                    ScheduleRetry();
                }

                return;
            }

            bool discard;

            lock (_lock)
            {
                // The feed closed while the subscription was being made.
                discard = _attempt != attempt;

                if (!discard)
                {
                    _subscription = subscription;

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

        private void OnTrade(object attempt, TradeEvent trade)
        {
            lock (_lock)
            {
                if (_attempt != attempt)
                {
                    return;
                }

                var started = TradeFlowMinute.Start(trade);
                bool newMinute = _latest is null || started.OpenTime > _latest.Minute.OpenTime;

                if (!newMinute && started.OpenTime < _latest!.Minute.OpenTime)
                {
                    return;
                }

                _latest = new LiveTradeFlow(newMinute ? started : _latest!.Minute.Add(trade), _since);
                SetState(FormingState.Live);
                ArmSilence();
                DateTimeOffset now = time.GetUtcNow();

                if (newMinute || now - _sentAt >= Period)
                {
                    Send(now);
                }
                else
                {
                    _held ??= time.CreateTimer(_ => SendHeld(), null, _sentAt + Period - now, Timeout.InfiniteTimeSpan);
                }
            }
        }

        private void SendHeld()
        {
            lock (_lock)
            {
                Send(time.GetUtcNow());
            }
        }

        private void Send(DateTimeOffset now)
        {
            _held?.Dispose();
            _held = null;
            _sentAt = now;
            Observe(broadcast.MinuteAsync(instrument, _latest!));
        }

        // A restored connection says nothing here: the feed stays without data until the next trade.
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
            _silence ??= time.CreateTimer(_ => OnSilence(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _silence.Change(Silence, Timeout.InfiniteTimeSpan);
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
            _retry = time.CreateTimer(_ => _ = ConnectAsync(), null, Retry, Timeout.InfiniteTimeSpan);
        }

        private void SetState(FormingState state)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            Observe(broadcast.StatusAsync(instrument, state));
        }

        private static void Observe(Task send)
        {
            // A failed send is not the feed's: the next update or the snapshot of a new viewer tells the viewers again.
            send.ContinueWith(static task => task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}