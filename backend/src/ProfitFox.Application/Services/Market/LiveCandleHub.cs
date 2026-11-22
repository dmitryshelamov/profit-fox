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
    ///     The live candles of <paramref name="symbol" /> on <paramref name="timeframe" /> until the page stops listening;
    ///     nothing for a pair outside the set.
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
            int elapsed = (int)Math.Ceiling((now - window) / OneMinute);
            if (elapsed > 0)
            {
                RecentRead<HistoryCandle> seed = await history.Candles(pair.Id, OneMinute, now, Math.Min(elapsed, 1000), cancellation);
                foreach (HistoryCandle candle in seed.Items.Where(candle => candle.OpenTime >= window && candle.HasTrades))
                {
                    held[candle.OpenTime] = Minute(symbol, candle);
                }
            }

            DateTimeOffset sent = DateTimeOffset.MinValue;
            bool changed = held.Count > 0;
            Task<bool>? waiting = null;
            while (!cancellation.IsCancellationRequested)
            {
                // One wait for the channel at a time: a reader of one consumer takes no second one while the first waits.
                waiting = waiting is { IsCompleted: false } ? waiting : minutes.Reader.WaitToReadAsync(cancellation).AsTask();
                await Task.WhenAny(waiting, Task.Delay(Throttle, time, cancellation));
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
                _pump = Pump(_subscription.Token);
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
    private async Task Pump(CancellationToken cancellation)
    {
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
        catch (OperationCanceledException)
        {
            // The last page stopped listening.
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
