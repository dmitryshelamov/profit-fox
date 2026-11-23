using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Clients.MarketData.Protos;

namespace ProfitFox.Infrastructure.Clients.MarketData;

/// <summary>
///     The live feeds over the <c>Subscribe</c> of the four feeds of the market-data service (change add-ideas-and-market,
///     design.md, decision 2; spec market-live-feed). It subscribes to Bybit and keeps only the pairs of the catalog of
///     linear USDT perpetuals, read again every <see cref="MarketFeedSettings.CatalogRefresh" />. What comes is written to a
///     bounded queue of its own: a slow reader stops the reading, and a stream the service cuts off for it is subscribed
///     again from its last item. After <c>UNAVAILABLE</c>,
///     <c>RESOURCE_EXHAUSTED</c> or a break it subscribes again from a little before the last item it got; a break longer
///     than the service catches up is told as a <see cref="FeedGap" />, once for a break. Whatever else breaks a feed — but
///     a cancellation — is written to the journal, and the feed subscribes again after the pause of a retry; an item it
///     cannot read is written to the journal and left out (task 18.7). A stream of minutes that tells nothing for longer than
///     <see cref="MarketFeedSettings.FeedSilence" /> while the feed waits for it is broken off with a warning and subscribed
///     again as after a break (task 24.1).
/// </summary>
public sealed class GrpcMarketFeed(GrpcChannel channel, IInstrumentCatalog catalog, TimeProvider time, MarketFeedSettings settings, ILogger? logger = null) : IMarketFeed
{
    public const string Minutes = "minutes";
    public const string Payments = "funding-payments";
    public const string Rates = "funding-rates";
    public const string OpenInterest = "open-interest";

    private readonly CandleFeed.CandleFeedClient _candles = new CandleFeed.CandleFeedClient(channel);
    private readonly FundingPaymentFeed.FundingPaymentFeedClient _payments = new FundingPaymentFeed.FundingPaymentFeedClient(channel);
    private readonly FundingRateFeed.FundingRateFeedClient _rates = new FundingRateFeed.FundingRateFeedClient(channel);
    private readonly OpenInterestFeed.OpenInterestFeedClient _openInterest = new OpenInterestFeed.OpenInterestFeedClient(channel);

    public async IAsyncEnumerable<MarketEvent> Subscribe(MarketSubscription subscription, [EnumeratorCancellation] CancellationToken cancellation)
    {
        // A bounded queue (task 18.12): a reader that is slow stops the reading of the streams instead of piling them up.
        var queue = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(Math.Max(1, settings.QueueCapacity))
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var pairs = new PairsView();
        await RefreshPairs(pairs, queue.Writer, stop.Token);
        List<Task> feeds =
        [
            Pairs(pairs, queue.Writer, stop.Token),
            Feed(
                Minutes,
                subscription,
                settings.Overlap,
                settings.FeedSilence > TimeSpan.Zero ? settings.FeedSilence : null,
                (since, token) => _candles.Subscribe(
                    new SubscribeRequest { Exchanges = { Exchange.Bybit }, Since = Stamp(since), Forming = subscription.Forming },
                    cancellationToken: token),
                candle => candle.Closed ? candle.OpenTime.ToDateTimeOffset() : null,
                candle => Minute(pairs, candle),
                queue.Writer,
                stop.Token)
        ];
        if (!subscription.MinutesOnly)
        {
            feeds.Add(Feed(
                Payments,
                subscription,
                settings.PaymentOverlap,
                silence: null,
                (since, token) => _payments.Subscribe(new SubscribeFundingPaymentRequest { Since = Stamp(since) }, cancellationToken: token),
                payment => payment.Time.ToDateTimeOffset(),
                payment => Point(pairs, LivePointKind.FundingPayment, payment.InstrumentId, payment.Time, payment.FundingRate),
                queue.Writer,
                stop.Token));
            feeds.Add(Feed(
                Rates,
                subscription,
                settings.Overlap,
                silence: null,
                (since, token) => _rates.Subscribe(new SubscribeFundingRateRequest { Since = Stamp(since) }, cancellationToken: token),
                rate => rate.Time.ToDateTimeOffset(),
                rate => Point(pairs, LivePointKind.FundingRate, rate.InstrumentId, rate.Time, rate.FundingRate),
                queue.Writer,
                stop.Token));
            feeds.Add(Feed(
                OpenInterest,
                subscription,
                settings.Overlap,
                silence: null,
                (since, token) => _openInterest.Subscribe(new SubscribeOpenInterestRequest { Since = Stamp(since) }, cancellationToken: token),
                point => point.Time.ToDateTimeOffset(),
                point => Point(pairs, LivePointKind.OpenInterest, point.InstrumentId, point.Time, point.OpenInterest),
                queue.Writer,
                stop.Token));
        }

        try
        {
            while (await queue.Reader.WaitToReadAsync(cancellation))
            {
                while (queue.Reader.TryRead(out MarketEvent? item))
                {
                    yield return item;
                }
            }
        }
        finally
        {
            await stop.CancelAsync();
            try
            {
                await Task.WhenAll(feeds);
            }
            catch (Exception exception) when (exception is OperationCanceledException or RpcException { StatusCode: StatusCode.Cancelled })
            {
                // The feeds stop with the subscription: a call cancelled by the client ends with CANCELLED.
            }
        }
    }

    // One feed: subscribed again after every break and the pause of a retry, from a little before the last item, or from
    // the limit of the catch-up with a hole told when the break was longer — one hole for a break, however many tries it
    // takes. With a <paramref name="silence" />, a call that waits for its next item longer than that is broken off as a
    // break (task 24.1); the time it waits for the reader is not counted.
    private async Task Feed<T>(
        string name,
        MarketSubscription subscription,
        TimeSpan overlap,
        TimeSpan? silence,
        Func<DateTimeOffset?, CancellationToken, AsyncServerStreamingCall<T>> open,
        Func<T, DateTimeOffset?> timeOf,
        Func<T, MarketEvent?> map,
        ChannelWriter<MarketEvent> queue,
        CancellationToken cancellation)
    {
        DateTimeOffset? start = subscription.Since;
        DateTimeOffset? last = null;
        bool holeTold = false;
        bool first = true;
        while (!cancellation.IsCancellationRequested)
        {
            DateTimeOffset limit = time.GetUtcNow() - settings.CatchUp + TimeSpan.FromMinutes(1);
            if (first && start is null && subscription.WholeDay)
            {
                // The whole day the service holds, counted now: the start has no hole to tell.
                start = limit;
            }

            first = false;
            DateTimeOffset? asked = start;
            if (asked is { } from && from < limit)
            {
                if (!holeTold)
                {
                    await queue.WriteAsync(new FeedGap(name, from, limit), cancellation);
                    holeTold = true;
                }

                asked = limit;
            }

            using Watchdog? watchdog = silence is { } most ? new Watchdog(time, most, cancellation) : null;
            try
            {
                CancellationToken token = watchdog?.Token ?? cancellation;
                using AsyncServerStreamingCall<T> call = open(asked, token);
                while (await Next(call.ResponseStream, watchdog, token))
                {
                    T item = call.ResponseStream.Current;
                    if (timeOf(item) is { } at && (last is null || at > last))
                    {
                        last = at;
                    }

                    holeTold = false;
                    if (Map(name, map, item) is { } mapped)
                    {
                        await queue.WriteAsync(mapped, cancellation);
                    }
                }
            }
            catch (Exception) when (watchdog is { Silent: { } silent } && !cancellation.IsCancellationRequested)
            {
                // The stream told nothing for longer than the limit, without an error: a half-open connection or a service
                // that hangs. It is broken off and subscribed again below, as after a break.
                DateTimeOffset? again = last is { } newest ? newest - overlap : asked;
                logger?.LogWarning(
                    "The feed {Feed} told nothing for {Silence}; it is broken off and subscribes again from {Since}.",
                    name,
                    silent.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
                    again?.ToString("O", CultureInfo.InvariantCulture) ?? "now");
            }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.InvalidArgument && asked is not null && !cancellation.IsCancellationRequested)
            {
                // The clock of the service runs ahead of ours: catch up from where it still holds, after the pause.
                DateTimeOffset holds = time.GetUtcNow() - settings.CatchUp + TimeSpan.FromMinutes(5);
                if (asked < holds && !holeTold)
                {
                    await queue.WriteAsync(new FeedGap(name, asked.Value, holds), cancellation);
                    holeTold = true;
                }

                logger?.LogWarning("The feed {Feed} refused since {Since:O}: {Detail}; it subscribes again from {Holds:O}.", name, asked, exception.Status.Detail, holds);
                start = holds;
                await Pause(cancellation);
                continue;
            }
            catch (Exception exception) when (!cancellation.IsCancellationRequested)
            {
                // UNAVAILABLE, RESOURCE_EXHAUSTED, a break or anything else: written down, and subscribed again below.
                if (exception is not (RpcException or IOException or HttpRequestException))
                {
                    logger?.LogError(exception, "The feed {Feed} failed; it subscribes again.", name);
                }
                else
                {
                    logger?.LogInformation("The feed {Feed} broke off ({Reason}); it subscribes again.", name, exception.Message);
                }
            }
            catch (Exception) when (cancellation.IsCancellationRequested)
            {
                return;
            }

            start = last is { } got ? got - overlap : asked;
            await Pause(cancellation);
        }
    }

    // The next item of a stream. Only a wait for the stream is watched: an item it had already given is no silence, and
    // neither is the time the feed waits for its reader between the items.
    private static async Task<bool> Next<T>(IAsyncStreamReader<T> stream, Watchdog? watchdog, CancellationToken cancellation)
    {
        Task<bool> next = stream.MoveNext(cancellation);
        if (watchdog is null || next.IsCompleted)
        {
            return await next;
        }

        watchdog.Waiting();
        try
        {
            return await next;
        }
        finally
        {
            watchdog.Heard();
        }
    }

    // An item the feed cannot read is left out, and the journal says so.
    private MarketEvent? Map<T>(string name, Func<T, MarketEvent?> map, T item)
    {
        try
        {
            return map(item);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            logger?.LogWarning(exception, "The feed {Feed} sent an item it cannot read, and it is left out: {Item}", name, item);
            return null;
        }
    }

    private async Task Pause(CancellationToken cancellation)
    {
        if (settings.RetryDelay > TimeSpan.Zero)
        {
            await Task.Delay(settings.RetryDelay, time, cancellation);
        }
    }

    // The catalog read again every hour; a change is told.
    private async Task Pairs(PairsView pairs, ChannelWriter<MarketEvent> queue, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            await Task.Delay(settings.CatalogRefresh, time, cancellation);
            try
            {
                await RefreshPairs(pairs, queue, cancellation);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The catalog did not answer: the pairs stay as they were until the next reading.
            }
        }
    }

    private async Task RefreshPairs(PairsView pairs, ChannelWriter<MarketEvent> queue, CancellationToken cancellation)
    {
        IReadOnlyList<Instrument> listed = await catalog.LinearUsdtPerpetuals(cancellation);
        if (pairs.Set(listed) is { } kept)
        {
            await queue.WriteAsync(new MarketPairs(kept), cancellation);
        }
    }

    private static LiveMinute? Minute(PairsView pairs, CandleUpdate candle)
    {
        if (candle.Exchange != Exchange.Bybit || pairs.Symbol(candle.InstrumentId) is not { } symbol)
        {
            return null;
        }

        decimal close = Parse(candle.Close);
        decimal volume = Parse(candle.Volume);
        return new LiveMinute(
            symbol,
            candle.OpenTime.ToDateTimeOffset(),
            Parse(candle.Open),
            Parse(candle.High),
            Parse(candle.Low),
            close,
            volume,
            candle.HasTurnover ? Parse(candle.Turnover) : volume * close,
            candle.Closed);
    }

    private static LivePoint? Point(PairsView pairs, LivePointKind kind, string instrument, Timestamp time, string value)
    {
        return pairs.Symbol(instrument) is { } symbol ? new LivePoint(kind, symbol, time.ToDateTimeOffset(), Parse(value)) : null;
    }

    private static Timestamp? Stamp(DateTimeOffset? since)
    {
        return since is { } moment ? Timestamp.FromDateTimeOffset(moment) : null;
    }

    private static decimal Parse(string text)
    {
        return decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // The watchdog of one call (task 24.1): once the call waited for its next item longer than the limit, its token is
    // cancelled and <see cref="Silent" /> tells how long it waited. One timer for the call, set when a wait starts.
    private sealed class Watchdog : IDisposable
    {
        private const long NotWaiting = long.MaxValue;
        private readonly TimeProvider _time;
        private readonly TimeSpan _limit;
        private readonly CancellationTokenSource _stop;
        private readonly ITimer _timer;
        private long _since = NotWaiting;
        private long _silent = -1;

        public Watchdog(TimeProvider time, TimeSpan limit, CancellationToken cancellation)
        {
            _time = time;
            _limit = limit;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            _timer = time.CreateTimer(_ => Check(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public CancellationToken Token => _stop.Token;

        // How long the call waited when it was broken off; null while it was not.
        public TimeSpan? Silent => Interlocked.Read(ref _silent) is >= 0 and var ticks ? TimeSpan.FromTicks(ticks) : null;

        public void Waiting()
        {
            Interlocked.Exchange(ref _since, _time.GetUtcNow().UtcTicks);
            _timer.Change(_limit, Timeout.InfiniteTimeSpan);
        }

        public void Heard()
        {
            Interlocked.Exchange(ref _since, NotWaiting);
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            _timer.Dispose();
            _stop.Dispose();
        }

        // The timer of a former wait may come late: the wait of now is measured from its own start.
        private void Check()
        {
            long since = Interlocked.Read(ref _since);
            if (since == NotWaiting)
            {
                return;
            }

            TimeSpan waited = _time.GetUtcNow() - new DateTimeOffset(since, TimeSpan.Zero);
            if (waited < _limit)
            {
                _timer.Change(_limit - waited, Timeout.InfiniteTimeSpan);
                return;
            }

            Interlocked.Exchange(ref _silent, waited.Ticks);
            try
            {
                _stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The call ended meanwhile.
            }
        }
    }

    // The pairs by the id of the catalog, read by the feeds while the catalog task replaces them. A pair left out of one
    // answer of the catalog is kept; it is dropped when the next answer leaves it out too (task 18.8): one answer without a
    // pair does not exclude its open ideas.
    private sealed class PairsView
    {
        private volatile Dictionary<string, string> _symbols = new Dictionary<string, string>(StringComparer.Ordinal);
        private List<Instrument> _kept = [];
        private HashSet<string> _missedOnce = new HashSet<string>(StringComparer.Ordinal);
        private bool _read;

        public string? Symbol(string instrument)
        {
            return _symbols.GetValueOrDefault(instrument);
        }

        // The pairs kept after this answer, when they changed; null when they did not.
        public IReadOnlyList<Instrument>? Set(IReadOnlyList<Instrument> listed)
        {
            var ids = new HashSet<string>(listed.Select(pair => pair.Id), StringComparer.Ordinal);
            List<Instrument> missed = _read ? [.. _kept.Where(pair => !ids.Contains(pair.Id) && !_missedOnce.Contains(pair.Id))] : [];
            List<Instrument> kept = [.. listed, .. missed];
            _missedOnce = new HashSet<string>(missed.Select(pair => pair.Id), StringComparer.Ordinal);
            _read = true;
            var symbols = kept.ToDictionary(pair => pair.Id, pair => pair.Symbol, StringComparer.Ordinal);
            bool changed = symbols.Count != _symbols.Count || symbols.Any(pair => _symbols.GetValueOrDefault(pair.Key) != pair.Value);
            _symbols = symbols;
            _kept = kept;
            return changed ? kept : null;
        }
    }
}
