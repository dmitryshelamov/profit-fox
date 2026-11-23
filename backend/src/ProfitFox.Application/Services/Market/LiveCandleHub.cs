using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Services.Market;

/// <summary>
///     The live candles of the pages of pairs, one for the process of the API (change add-ideas-and-market, design.md,
///     decision 9): while at least one page listens, it keeps one subscription of the minutes of the feed with the minute that
///     goes on, and gives each page the candle of its timeframe built from the minutes of the window — the closed ones of the
///     window read from the REST of the service when the page starts listening, then the live ones — no more than once a
///     second, and the closed candle when the clock passes the end of the window. With the last page the subscription ends.
/// </summary>
public sealed class LiveCandleHub(IMarketFeed feed, IRecentHistory history, IInstrumentCatalog catalog, TimeProvider time)
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Throttle = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SeedWindow = TimeSpan.FromMinutes(1000);

    private readonly Lock _lock = new Lock();
    private readonly Dictionary<string, List<Channel<LiveMinute>>> _listeners = new Dictionary<string, List<Channel<LiveMinute>>>(StringComparer.Ordinal);
    private CancellationTokenSource? _subscription;
    private Task _pump = Task.CompletedTask;

    /// <summary>
    ///     How many pages listen now.
    /// </summary>
    public int Listeners
    {
        get
        {
            lock (_lock)
            {
                return _listeners.Values.Sum(list => list.Count);
            }
        }
    }

    /// <summary>
    ///     Whether the pair is in the set: the page of a pair outside it gets 404 before any stream (task 18.11).
    /// </summary>
    public async Task<bool> IsListed(string symbol, CancellationToken cancellation)
    {
        return (await catalog.LinearUsdtPerpetuals(cancellation)).Any(pair => pair.Symbol == symbol);
    }

    /// <summary>
    ///     The live candles of <paramref name="symbol" /> on <paramref name="timeframe" /> until the page stops listening;
    ///     nothing for a pair outside the set. A failure of the feed ends the candles with its exception.
    /// </summary>
    public async IAsyncEnumerable<LiveCandle> Listen(string symbol, TimeSpan timeframe, [EnumeratorCancellation] CancellationToken cancellation)
    {
        if ((await catalog.LinearUsdtPerpetuals(cancellation)).FirstOrDefault(pair => pair.Symbol == symbol) is not { } pair)
        {
            yield break;
        }

        Channel<LiveMinute> minutes = Add(symbol);
        try
        {
            DateTimeOffset now = time.GetUtcNow();
            DateTimeOffset window = Floor(now, timeframe);
            var held = new SortedDictionary<DateTimeOffset, LiveMinute>();
            // Every closed minute of the window, in windows of at most a read each (task 18.11): a day after 16:40 has more
            // than 1000.
            DateTimeOffset end = Floor(now, OneMinute);
            // The close of the last minute read, with trades or without: the price of a window without a trade yet.
            decimal? price = null;
            for (DateTimeOffset from = window; from < end; from += SeedWindow)
            {
                DateTimeOffset to = from + SeedWindow < end ? from + SeedWindow : end;
                RecentRead<HistoryCandle> seed = await history.Candles(pair.Id, OneMinute, to, (int)((to - from) / OneMinute), cancellation);
                foreach (HistoryCandle candle in seed.Items.Where(candle => candle.OpenTime >= from && candle.OpenTime < to))
                {
                    price = candle.ToClosedCandle(OneMinute).Close;
                    if (candle.HasTrades)
                    {
                        held[candle.OpenTime] = Minute(symbol, candle);
                    }
                }
            }

            if (held.Count == 0 && price is null)
            {
                // The window has no closed minute yet: the price is the close of the minute before it.
                RecentRead<HistoryCandle> before = await history.Candles(pair.Id, OneMinute, window, 1, cancellation);
                price = before.Items.Where(candle => candle.OpenTime < window).Select(candle => (decimal?)candle.ToClosedCandle(OneMinute).Close).LastOrDefault();
            }

            // Right after the page subscribed it gets the candle of the seed (task 19.5): a quiet pair may tell no minute for a
            // while. A window without a trade yet is a flat candle at the last close, as the service tells a minute without one.
            // The seed is no update of the feed: the first minute of the feed goes without waiting for the second.
            if ((Candle(window, held, closed: false) ?? Flat(window, price)) is { } first)
            {
                yield return first;
            }

            DateTimeOffset sent = DateTimeOffset.MinValue;

            bool changed = false;
            Task<bool>? waiting = null;
            while (!cancellation.IsCancellationRequested)
            {
                // One wait for the channel at a time: a reader of one consumer takes no second one while the first waits.
                waiting = waiting is { IsCompleted: false } ? waiting : minutes.Reader.WaitToReadAsync(cancellation).AsTask();
                await Task.WhenAny(waiting, Task.Delay(Throttle, time, cancellation));
                // The feed failed or ended: the page gets the error and connects again (task 18.11).
                if (waiting.IsFaulted)
                {
                    await waiting;
                }

                if (waiting.IsCompletedSuccessfully && !waiting.Result)
                {
                    throw new InvalidOperationException("The live feed of the market-data service ended.");
                }

                while (minutes.Reader.TryRead(out LiveMinute? minute))
                {
                    if (minute.OpenTime >= window + timeframe)
                    {
                        if (Candle(window, held, closed: true) is { } last)
                        {
                            yield return last;
                        }

                        window = Floor(minute.OpenTime, timeframe);
                        held.Clear();
                    }

                    if (minute.OpenTime >= window)
                    {
                        held[minute.OpenTime] = minute;
                        changed = true;
                    }
                }

                now = time.GetUtcNow();
                if (now >= window + timeframe)
                {
                    if (Candle(window, held, closed: true) is { } last)
                    {
                        yield return last;
                    }

                    window = Floor(now, timeframe);
                    held.Clear();
                    changed = false;
                }

                if (changed && now - sent >= Throttle && Candle(window, held, closed: false) is { } live)
                {
                    sent = now;
                    changed = false;
                    yield return live;
                }
            }
        }
        finally
        {
            Remove(symbol, minutes);
        }
    }

    private Channel<LiveMinute> Add(string symbol)
    {
        var channel = Channel.CreateUnbounded<LiveMinute>(new UnboundedChannelOptions { SingleReader = true });
        lock (_lock)
        {
            if (!_listeners.TryGetValue(symbol, out List<Channel<LiveMinute>>? list))
            {
                list = [];
                _listeners[symbol] = list;
            }

            list.Add(channel);
            if (_subscription is null)
            {
                _subscription = new CancellationTokenSource();
                _pump = Pump(_subscription);
            }
        }

        return channel;
    }

    private void Remove(string symbol, Channel<LiveMinute> channel)
    {
        lock (_lock)
        {
            if (_listeners.TryGetValue(symbol, out List<Channel<LiveMinute>>? list))
            {
                list.Remove(channel);
                if (list.Count == 0)
                {
                    _listeners.Remove(symbol);
                }
            }

            if (_listeners.Count == 0 && _subscription is { } subscription)
            {
                subscription.Cancel();
                subscription.Dispose();
                _subscription = null;
            }
        }
    }

    // The one subscription of the minutes, the one that goes on included, while pages listen.
    private async Task Pump(CancellationTokenSource source)
    {
        CancellationToken cancellation = source.Token;
        try
        {
            await foreach (MarketEvent item in feed.Subscribe(new MarketSubscription(Since: null, Forming: true, MinutesOnly: true), cancellation))
            {
                if (item is not LiveMinute minute)
                {
                    continue;
                }

                lock (_lock)
                {
                    if (_listeners.TryGetValue(minute.Symbol, out List<Channel<LiveMinute>>? list))
                    {
                        foreach (Channel<LiveMinute> channel in list)
                        {
                            channel.Writer.TryWrite(minute);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The last page stopped listening.
            return;
        }
        catch (Exception exception)
        {
            Fail(source, exception);
            return;
        }

        Fail(source, new InvalidOperationException("The live feed of the market-data service ended."));
    }

    // The subscription is gone: every page gets the failure, and the next page subscribes again (task 18.11). Only the
    // current subscription fails its pages (task 19.5): a former one, cancelled when its last page left, may end after a new
    // one began, and must not end the pages of the new one or leave its stream with no one to cancel it.
    private void Fail(CancellationTokenSource source, Exception exception)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_subscription, source))
            {
                return;
            }

            foreach (Channel<LiveMinute> channel in _listeners.Values.SelectMany(list => list))
            {
                channel.Writer.TryComplete(exception);
            }

            _subscription?.Dispose();
            _subscription = null;
        }
    }

    private LiveCandle? Candle(DateTimeOffset window, SortedDictionary<DateTimeOffset, LiveMinute> held, bool closed)
    {
        if (held.Count == 0)
        {
            return null;
        }

        LiveMinute[] minutes = [.. held.Values];
        return new LiveCandle(
            window,
            minutes[0].Open,
            minutes.Max(minute => minute.High),
            minutes.Min(minute => minute.Low),
            minutes[^1].Close,
            minutes.Sum(minute => minute.Volume),
            closed,
            time.GetUtcNow());
    }

    private LiveCandle? Flat(DateTimeOffset window, decimal? price)
    {
        return price is { } close ? new LiveCandle(window, close, close, close, close, 0, false, time.GetUtcNow()) : null;
    }

    private static LiveMinute Minute(string symbol, HistoryCandle candle)
    {
        var closed = candle.ToClosedCandle(OneMinute);
        return new LiveMinute(symbol, candle.OpenTime, closed.Open, closed.High, closed.Low, closed.Close, closed.Volume, closed.Volume * closed.Close);
    }

    private static DateTimeOffset Floor(DateTimeOffset moment, TimeSpan step)
    {
        return new DateTimeOffset(moment.UtcTicks - (moment.UtcTicks % step.Ticks), TimeSpan.Zero);
    }
}
