using Grpc.Core;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Tinkoff.InvestApi.V1;

using Candle = ProfitFox.Domain.Entities.MarketData.Candle;
using ExchangeOrderState = ProfitFox.Application.Ports.LiveTrading.ExchangeOrderState;
using Instrument = ProfitFox.Domain.ValueObjects.MarketData.Instrument;
using Interval = ProfitFox.Domain.Entities.MarketData.Enums.Interval;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The streams of T-Invest of the process (decision 9): one <c>OrderStateStream</c> an account, however many bots listen to it, and
///     two <c>MarketDataStream</c>s, of the closed candles and of the forming ones, with one subscription a series for all its listeners.
///     A stream that ends, breaks or is silent for <see cref="Silence" /> (it pings every 30 s) opens again after 1, 2, 5, 10, then 30 s,
///     subscribes its series again and tells every listener <c>restored</c>. An event of the stream of orders is a signal: the state of
///     the order is read by its request id, again while T-Invest does not know it yet, and the end of a filled order waits up to a minute
///     for its commission; an order that does not read tells every listener <c>restored</c>. One for the process.
/// </summary>
internal sealed class TInvestStreams(
    ITInvestCallInvokers invokers,
    TInvestSettings settings,
    TInvestMetrics metrics,
    TInvestCall call,
    TInvestTokenSource tokens,
    TInvestShares shares,
    ITInvestOrderIds ids,
    TInvestTradingStatuses statuses,
    TimeProvider time) : IExchangeStreams, IFormingCandleStreams
{
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(90);

    private static readonly TimeSpan StateRetry = TimeSpan.FromSeconds(1);

    private const int StateReads = 5;

    /// <summary>The times after the fill at which a filled order without its commission is read again: 5, 15, 30 and 60 s.</summary>
    private static readonly TimeSpan[] FeeReads = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)];

    /// <summary>How long after the end of a trading day by the schedule the listeners of its closed candles are told <c>restored</c>.</summary>
    private static readonly TimeSpan DayEndMargin = TimeSpan.FromMinutes(1);

    private const int PingMilliseconds = 30_000;

    private static readonly TimeSpan[] Pauses = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    private static readonly TimeSpan NoSchedulePause = TimeSpan.FromHours(1);

    // What the streams and the series take of their owner.
    private CallInvoker Invoker => invokers.Invoker;

    private TInvestMetrics Metrics => metrics;

    private TInvestTokenSource Tokens => tokens;

    private TInvestTradingStatuses Statuses => statuses;

    private TimeProvider Time => time;

    private readonly Lock _lock = new Lock();
    private readonly Dictionary<string, AccountStream> _accounts = [];
    private CandleStream? _closed;
    private CandleStream? _forming;

    public async Task<IAsyncDisposable> SubscribeCandlesAsync(Instrument instrument, Interval interval, Action<Candle> closed, Action restored, CancellationToken cancellationToken)
    {
        TInvestShare share = await shares.ShareByAsync(instrument.Symbol, cancellationToken);

        return await Candles(waitingClose: true).JoinAsync(share, interval, new CandleListener(closed, null, restored, null), cancellationToken);
    }

    public async Task<IAsyncDisposable> SubscribeAsync(
        Instrument instrument,
        Interval interval,
        Action<FormingCandle> update,
        Action lost,
        Action restored,
        Action closed,
        CancellationToken cancellationToken)
    {
        TInvestShare share = await shares.ShareByAsync(instrument.Symbol, cancellationToken);

        return await Candles(waitingClose: false).JoinAsync(share, interval, new CandleListener(null, update, restored, lost), cancellationToken);
    }

    public async Task<IAsyncDisposable> SubscribeAccountAsync(
        ExchangeKey key,
        Action<ExchangeOrder> order,
        Action<ExchangeFill> fill,
        Action restored,
        CancellationToken cancellationToken)
    {
        AccountStream stream;

        lock (_lock)
        {
            stream = _accounts.TryGetValue(key.ApiKey, out AccountStream? known) ? known : _accounts[key.ApiKey] = new AccountStream(this, key);
        }

        var listener = new AccountListener(order, fill, restored);
        await stream.JoinAsync(listener, cancellationToken);

        return new Leave(() => stream.LeaveAsync(listener));
    }

    private CandleStream Candles(bool waitingClose)
    {
        lock (_lock)
        {
            return waitingClose ? _closed ??= new CandleStream(this, true) : _forming ??= new CandleStream(this, false);
        }
    }

    private CallOptions Options(string token, CancellationToken cancellationToken)
    {
        return new CallOptions(new Metadata { { "authorization", $"Bearer {token}" }, { "x-app-name", settings.AppName } }, cancellationToken: cancellationToken);
    }

    /// <summary>The next message of a stream; a stream silent for <see cref="Silence" />, pings included, is lost.</summary>
    private async Task<bool> NextAsync<T>(IAsyncStreamReader<T> reader, CancellationToken stop)
    {
        using var silence = new CancellationTokenSource(Silence, time);
        using var both = CancellationTokenSource.CreateLinkedTokenSource(stop, silence.Token);

        try
        {
            return await reader.MoveNext(both.Token);
        }
        catch (OperationCanceledException) when (silence.IsCancellationRequested && !stop.IsCancellationRequested)
        {
            throw new TimeoutException("The stream of T-Invest is silent.");
        }
    }

    private Task PauseAsync(int failures, CancellationToken stop)
    {
        return Task.Delay(Pauses[Math.Min(failures, Pauses.Length - 1)], time, stop);
    }

    private static void Tell<T>(IReadOnlyList<T> listeners, Action<T> what)
    {
        foreach (T listener in listeners)
        {
            what(listener);
        }
    }

    private sealed record AccountListener(Action<ExchangeOrder> Order, Action<ExchangeFill> Fill, Action Restored);

    private sealed record CandleListener(Action<Candle>? Closed, Action<FormingCandle>? Update, Action Restored, Action? Lost);

    private sealed class Leave(Func<ValueTask> leave) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return leave();
        }
    }

    /// <summary>The listeners of one stream; they come and go under the gate, the stream reads them meanwhile, so each change makes a new array.</summary>
    private sealed class Listeners<T>
        where T : class
    {
        private T[] _items = [];

        public IReadOnlyList<T> Items => Volatile.Read(ref _items);

        public void Add(T item)
        {
            Volatile.Write(ref _items, [.. _items, item]);
        }

        public void Remove(T item)
        {
            Volatile.Write(ref _items, [.. _items.Where(other => !ReferenceEquals(other, item))]);
        }
    }

    /// <summary>The stream of the orders of one account: the first listener opens it, the last one closes it.</summary>
    private sealed class AccountStream(TInvestStreams owner, ExchangeKey key)
    {
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly Listeners<AccountListener> _listeners = new Listeners<AccountListener>();
        private CancellationTokenSource? _stop;
        private Task _loop = Task.CompletedTask;

        public async Task JoinAsync(AccountListener listener, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);

            try
            {
                _listeners.Add(listener);

                if (_stop is null)
                {
                    _stop = new CancellationTokenSource();
                    _loop = RunAsync(_stop.Token);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask LeaveAsync(AccountListener listener)
        {
            await _gate.WaitAsync();

            try
            {
                _listeners.Remove(listener);

                if (_listeners.Items.Count == 0 && _stop is { } stop)
                {
                    _stop = null;
                    await stop.CancelAsync();
                    await _loop;
                    stop.Dispose();
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task RunAsync(CancellationToken stop)
        {
            await Task.Yield();
            var client = new OrdersStreamService.OrdersStreamServiceClient(owner.Invoker);
            var lost = false;
            var failures = 0;

            while (!stop.IsCancellationRequested)
            {
                var established = false;

                try
                {
                    var request = new OrderStateStreamRequest { Accounts = { key.ApiKey }, PingDelayMillis = PingMilliseconds };
                    using AsyncServerStreamingCall<OrderStateStreamResponse> stream = client.OrderStateStream(request, owner.Options(key.Secret, stop));

                    while (await owner.NextAsync(stream.ResponseStream, stop))
                    {
                        if (!established)
                        {
                            established = true;
                            failures = 0;
                            owner.Metrics.Count(TInvestMethodGroup.Orders, "OrderStateStream", "ok");

                            if (lost)
                            {
                                Tell(_listeners.Items, listener => listener.Restored());
                            }
                        }

                        if (stream.ResponseStream.Current.PayloadCase == OrderStateStreamResponse.PayloadOneofCase.OrderState)
                        {
                            _ = owner.OrderAsync(key, stream.ResponseStream.Current.OrderState, _listeners, stop);
                        }
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (exception is RpcException or TimeoutException)
                {
                    // Lost: opened again below.
                }

                owner.Metrics.Count(TInvestMethodGroup.Orders, "OrderStateStream", established ? "lost" : "error");
                lost = true;

                try
                {
                    await owner.PauseAsync(failures++, stop);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    ///     A message of the stream of orders, told by <see cref="ReadOrderAsync" />; a message whose order does not read (T-Invest, the
    ///     table of the ids or the share) counts as an error of the stream and tells every listener <c>restored</c>, so the sessions read
    ///     their orders themselves and no fill is lost without a word.
    /// </summary>
    private async Task OrderAsync(ExchangeKey key, OrderStateStreamResponse.Types.OrderState update, Listeners<AccountListener> listeners, CancellationToken stop)
    {
        try
        {
            await ReadOrderAsync(key, update, listeners, stop);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The last listener left.
        }
        catch (Exception)
        {
            metrics.Count(TInvestMethodGroup.Orders, "OrderStateStream", "error");
            Tell(listeners.Items, listener => listener.Restored());
        }
    }

    /// <summary>
    ///     What a message of the stream of orders tells (decision 7): an order of nobody in the system that traded is a fill of nobody on its
    ///     share; an order of a bot is read again by its request id, while T-Invest does not know it yet or its trades do not add up, and a
    ///     filled one without its commission yet again later, so its end comes with the commission.
    /// </summary>
    private async Task ReadOrderAsync(ExchangeKey key, OrderStateStreamResponse.Types.OrderState update, Listeners<AccountListener> listeners, CancellationToken stop)
    {
        var instrument = new Instrument(MarketCategory.Moex, update.Ticker);
        string? clientId = Guid.TryParse(update.OrderRequestId, out Guid request) ? await ids.ClientIdOfAsync(request, stop) : null;

        if (clientId is null)
        {
            foreach (OrderTrade trade in update.Trades)
            {
                var fill = new ExchangeFill(trade.TradeId, "", trade.Quantity * update.LotSize, trade.Price.ToDecimal(), 0m, TInvestOrderStates.Roubles, trade.DateTime.ToUtcMoment(), instrument, update.OrderId);
                Tell(listeners.Items, listener => listener.Fill(fill));
            }

            return;
        }

        if (await StateAsync(key, request, stop) is not { } state)
        {
            return;
        }

        int lot = (await shares.ShareByAsync(update.Ticker, stop)).Lot;

        foreach (TimeSpan pause in FeeReads)
        {
            if (TInvestOrderStates.Order(state, clientId, lot) is not { State: ExchangeOrderState.Filled, Fee: 0m })
            {
                break;
            }

            await Task.Delay(pause, time, stop);
            state = await StateAsync(key, request, stop) ?? state;
        }

        ExchangeOrder order = TInvestOrderStates.Order(state, clientId, lot);
        Tell(listeners.Items, listener => listener.Order(order));

        foreach (ExchangeFill fill in TInvestOrderStates.Fills(state, clientId, lot, instrument))
        {
            Tell(listeners.Items, listener => listener.Fill(fill));
        }
    }

    /// <summary>The state of an order by its request id, read up to <see cref="StateReads" /> times while T-Invest does not know it or its parts disagree.</summary>
    private async Task<OrderState?> StateAsync(ExchangeKey key, Guid request, CancellationToken stop)
    {
        for (var read = 1; ; read++)
        {
            OrderState? state = await TInvestOrderReads.StateAsync(call, key, request.ToString(), OrderIdType.Request, stop);

            if ((state is not null && TInvestOrderStates.Complete(state)) || read == StateReads)
            {
                return state;
            }

            await Task.Delay(StateRetry, time, stop);
        }
    }

    /// <summary>
    ///     One <c>MarketDataStream</c> of the closed candles or of the forming ones: a series is subscribed when its first listener comes and
    ///     taken off when its last one leaves, and every series again when the stream opens again.
    /// </summary>
    private sealed class CandleStream(TInvestStreams owner, bool waitingClose)
    {
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<(string Uid, SubscriptionInterval Interval), Series> _series = [];
        private IClientStreamWriter<MarketDataRequest>? _writer;
        private Task? _loop;

        public async Task<IAsyncDisposable> JoinAsync(TInvestShare share, Interval interval, CandleListener listener, CancellationToken cancellationToken)
        {
            (string, SubscriptionInterval) key = (share.Uid, interval.ToSubscription());
            await _gate.WaitAsync(cancellationToken);
            Series series;

            try
            {
                if (!_series.TryGetValue(key, out series!))
                {
                    series = new Series(share, interval);

                    lock (_series)
                    {
                        _series[key] = series;
                    }

                    if (_writer is { } writer)
                    {
                        await writer.WriteAsync(Request(SubscriptionAction.Subscribe, [series]));
                    }

                    if (waitingClose)
                    {
                        series.WatchDayEnds(owner);
                    }
                }

                series.Listeners.Add(listener);
                _loop ??= RunAsync();
            }
            finally
            {
                _gate.Release();
            }

            return new Leave(() => LeaveAsync(key, series, listener));
        }

        private async ValueTask LeaveAsync((string, SubscriptionInterval) key, Series series, CandleListener listener)
        {
            await _gate.WaitAsync();

            try
            {
                series.Listeners.Remove(listener);

                bool last;

                lock (_series)
                {
                    last = series.Listeners.Items.Count == 0 && _series.Remove(key);
                }

                if (last)
                {
                    await series.StopAsync();

                    if (_writer is { } writer)
                    {
                        await writer.WriteAsync(Request(SubscriptionAction.Unsubscribe, [series]));
                    }
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private MarketDataRequest Request(SubscriptionAction action, IEnumerable<Series> series)
        {
            return new MarketDataRequest
            {
                SubscribeCandlesRequest = new SubscribeCandlesRequest
                {
                    SubscriptionAction = action,
                    WaitingClose = waitingClose,
                    CandleSourceType = GetCandlesRequest.Types.CandleSource.Exchange,
                    Instruments = { series.Select(item => new CandleInstrument { InstrumentId = item.Share.Uid, Interval = item.Interval.ToSubscription() }) }
                }
            };
        }

        private async Task RunAsync()
        {
            await Task.Yield();
            var client = new MarketDataStreamService.MarketDataStreamServiceClient(owner.Invoker);
            var lost = false;
            var failures = 0;

            while (true)
            {
                var established = false;

                try
                {
                    string token = await owner.Tokens.GetAsync(CancellationToken.None);
                    using AsyncDuplexStreamingCall<MarketDataRequest, MarketDataResponse> stream = client.MarketDataStream(owner.Options(token, CancellationToken.None));
                    await _gate.WaitAsync();

                    try
                    {
                        await stream.RequestStream.WriteAsync(new MarketDataRequest { PingSettings = new PingDelaySettings { PingDelayMs = PingMilliseconds } });

                        if (_series.Count > 0)
                        {
                            await stream.RequestStream.WriteAsync(Request(SubscriptionAction.Subscribe, _series.Values));
                        }

                        _writer = stream.RequestStream;
                    }
                    finally
                    {
                        _gate.Release();
                    }

                    while (await owner.NextAsync(stream.ResponseStream, CancellationToken.None))
                    {
                        if (!established)
                        {
                            established = true;
                            failures = 0;
                            owner.Metrics.Count(TInvestMethodGroup.MarketData, "MarketDataStream", "ok");

                            if (lost)
                            {
                                Tell(All(), listener => listener.Restored());
                            }
                        }

                        if (stream.ResponseStream.Current.Candle is { } candle)
                        {
                            Dispatch(candle);
                        }
                    }
                }
                catch (Exception exception) when (exception is RpcException or TimeoutException or Application.Exceptions.ExchangeException)
                {
                    // Lost: opened again below.
                }

                await _gate.WaitAsync();
                _writer = null;
                _gate.Release();
                owner.Metrics.Count(TInvestMethodGroup.MarketData, "MarketDataStream", established ? "lost" : "error");

                Tell(All(), listener => listener.Lost?.Invoke());

                lost = true;
                await owner.PauseAsync(failures++, CancellationToken.None);
            }
        }

        private CandleListener[] All()
        {
            lock (_series)
            {
                return [.. _series.Values.SelectMany(series => series.Listeners.Items)];
            }
        }

        private void Dispatch(Tinkoff.InvestApi.V1.Candle update)
        {
            Series? series;

            lock (_series)
            {
                _series.TryGetValue((update.InstrumentUid, update.Interval), out series);
            }

            if (series is null)
            {
                return;
            }

            decimal high = update.High.ToDecimal();
            decimal low = update.Low.ToDecimal();
            decimal close = update.Close.ToDecimal();
            decimal volume = update.Volume * series.Share.Lot;
            DateTimeOffset open = update.Time.ToUtcMoment();

            if (waitingClose)
            {
                var candle = new Candle(open, new Price(update.Open.ToDecimal()), new Price(high), new Price(low), new Price(close), new Volume(volume), new Volume(volume * (high + low + close) / 3m));
                Tell(series.Listeners.Items, listener => listener.Closed?.Invoke(candle));
            }
            else
            {
                var forming = new FormingCandle(open, new Price(update.Open.ToDecimal()), new Price(high), new Price(low), new Price(close), new Volume(volume), false, update.LastTradeTs?.ToUtcMoment() ?? owner.Time.GetUtcNow());
                Tell(series.Listeners.Items, listener => listener.Update?.Invoke(forming));
            }
        }
    }

    /// <summary>A series of a share and an interval with its listeners and, for the closed candles, the watch of the ends of trading days.</summary>
    private sealed class Series(TInvestShare share, Interval interval)
    {
        private CancellationTokenSource? _stop;
        private Task _watch = Task.CompletedTask;

        public TInvestShare Share => share;

        public Interval Interval => interval;

        public Listeners<CandleListener> Listeners { get; } = new Listeners<CandleListener>();

        /// <summary>
        ///     At the end of every trading day of the share by the schedule, a minute after, its listeners are told <c>restored</c>: the
        ///     sessions read their orders again, so an order of the day that the exchange took off without a word ends then.
        /// </summary>
        public void WatchDayEnds(TInvestStreams owner)
        {
            _stop = new CancellationTokenSource();
            _watch = WatchAsync(owner, _stop.Token);
        }

        public async Task StopAsync()
        {
            if (_stop is { } stop)
            {
                await stop.CancelAsync();
                await _watch;
                stop.Dispose();
            }
        }

        private async Task<DateTimeOffset?> DayEndAsync(TInvestStreams owner, CancellationToken stop)
        {
            return await owner.Statuses.TradingDayEndAsync(await owner.Tokens.GetAsync(stop), share.Exchange, stop);
        }

        private async Task WatchAsync(TInvestStreams owner, CancellationToken stop)
        {
            await Task.Yield();

            try
            {
                while (true)
                {
                    DateTimeOffset? end = await DayEndAsync(owner, stop);

                    if (end is not { } dayEnd)
                    {
                        await Task.Delay(NoSchedulePause, owner.Time, stop);

                        continue;
                    }

                    await Task.Delay(dayEnd + DayEndMargin - owner.Time.GetUtcNow(), owner.Time, stop);
                    Tell(Listeners.Items, listener => listener.Restored());
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // The last listener left.
            }
        }
    }
}