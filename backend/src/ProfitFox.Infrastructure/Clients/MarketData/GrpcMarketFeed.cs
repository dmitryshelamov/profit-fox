using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.Client;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Clients.MarketData.Protos;

namespace ProfitFox.Infrastructure.Clients.MarketData;

/// <summary>
///     The live feeds over the <c>Subscribe</c> of the four feeds of the market-data service (change add-ideas-and-market,
///     design.md, decision 2; spec market-live-feed). It subscribes to Bybit and keeps only the pairs of the catalog of
///     linear USDT perpetuals, read again every <see cref="MarketFeedSettings.CatalogRefresh" />. What comes is written to a
///     queue of its own at once, so the service never cuts it off for a slow reader. After <c>UNAVAILABLE</c>,
///     <c>RESOURCE_EXHAUSTED</c> or a break it subscribes again from a little before the last item it got; a break longer
///     than the service catches up is told as a <see cref="FeedGap" />.
/// </summary>
public sealed class GrpcMarketFeed(GrpcChannel channel, IInstrumentCatalog catalog, TimeProvider time, MarketFeedSettings settings) : IMarketFeed
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
        var queue = Channel.CreateUnbounded<MarketEvent>(new UnboundedChannelOptions { SingleReader = true });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var pairs = new PairsView();
        await RefreshPairs(pairs, queue.Writer, stop.Token);
        List<Task> feeds =
        [
            Pairs(pairs, queue.Writer, stop.Token),
            Feed(
                Minutes,
                subscription.Since,
                settings.Overlap,
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
                subscription.Since,
                settings.PaymentOverlap,
                (since, token) => _payments.Subscribe(new SubscribeFundingPaymentRequest { Since = Stamp(since) }, cancellationToken: token),
                payment => payment.Time.ToDateTimeOffset(),
                payment => Point(pairs, LivePointKind.FundingPayment, payment.InstrumentId, payment.Time, payment.FundingRate),
                queue.Writer,
                stop.Token));
            feeds.Add(Feed(
                Rates,
                subscription.Since,
                settings.Overlap,
                (since, token) => _rates.Subscribe(new SubscribeFundingRateRequest { Since = Stamp(since) }, cancellationToken: token),
                rate => rate.Time.ToDateTimeOffset(),
                rate => Point(pairs, LivePointKind.FundingRate, rate.InstrumentId, rate.Time, rate.FundingRate),
                queue.Writer,
                stop.Token));
            feeds.Add(Feed(
                OpenInterest,
                subscription.Since,
                settings.Overlap,
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

    // One feed: subscribed again after every break, from a little before the last item, or from the limit of the
    // catch-up with a hole told when the break was longer.
    private async Task Feed<T>(
        string name,
        DateTimeOffset? since,
        TimeSpan overlap,
        Func<DateTimeOffset?, CancellationToken, AsyncServerStreamingCall<T>> open,
        Func<T, DateTimeOffset?> timeOf,
        Func<T, MarketEvent?> map,
        ChannelWriter<MarketEvent> queue,
        CancellationToken cancellation)
    {
        DateTimeOffset? start = since;
        DateTimeOffset? last = null;
        while (!cancellation.IsCancellationRequested)
        {
            DateTimeOffset? asked = start;
            DateTimeOffset limit = time.GetUtcNow() - settings.CatchUp + TimeSpan.FromMinutes(1);
            if (asked is { } from && from < limit)
            {
                await queue.WriteAsync(new FeedGap(name, from, limit), cancellation);
                asked = limit;
            }

            try
            {
                using AsyncServerStreamingCall<T> call = open(asked, cancellation);
                await foreach (T item in call.ResponseStream.ReadAllAsync(cancellation))
                {
                    if (timeOf(item) is { } at && (last is null || at > last))
                    {
                        last = at;
                    }

                    if (map(item) is { } mapped)
                    {
                        await queue.WriteAsync(mapped, cancellation);
                    }
                }
            }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.InvalidArgument && asked is not null && !cancellation.IsCancellationRequested)
            {
                // The clock of the service runs ahead of ours: catch up from where it still holds.
                DateTimeOffset holds = time.GetUtcNow() - settings.CatchUp + TimeSpan.FromMinutes(5);
                if (asked < holds)
                {
                    await queue.WriteAsync(new FeedGap(name, asked.Value, holds), cancellation);
                }

                start = holds;
                continue;
            }
            catch (Exception exception) when (exception is RpcException or IOException or HttpRequestException)
            {
                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                // UNAVAILABLE, RESOURCE_EXHAUSTED or a break: subscribe again below.
            }

            start = last is { } got ? got - overlap : asked;
            if (settings.RetryDelay > TimeSpan.Zero)
            {
                await Task.Delay(settings.RetryDelay, time, cancellation);
            }
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
        if (pairs.Set(listed))
        {
            await queue.WriteAsync(new MarketPairs(listed), cancellation);
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

    // The pairs by the id of the catalog, read by the feeds while the catalog task replaces them.
    private sealed class PairsView
    {
        private volatile Dictionary<string, string> _symbols = new Dictionary<string, string>(StringComparer.Ordinal);

        public string? Symbol(string instrument)
        {
            return _symbols.GetValueOrDefault(instrument);
        }

        // Whether the set changed.
        public bool Set(IReadOnlyList<Instrument> listed)
        {
            var symbols = listed.ToDictionary(pair => pair.Id, pair => pair.Symbol, StringComparer.Ordinal);
            bool changed = symbols.Count != _symbols.Count || symbols.Any(pair => _symbols.GetValueOrDefault(pair.Key) != pair.Value);
            _symbols = symbols;
            return changed;
        }
    }
}
