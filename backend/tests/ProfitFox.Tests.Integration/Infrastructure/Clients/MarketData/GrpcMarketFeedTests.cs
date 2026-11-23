using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Application.Services.Market;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Infrastructure.Clients.MarketData.Protos;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Logging;
using ProfitFox.Tests.Integration.Worker;

namespace ProfitFox.Tests.Integration.Infrastructure.Clients.MarketData;

/// <summary>
///     Change add-ideas-and-market, task 5.3: spec market-live-feed «Живые ленты сервиса данных» and «Догон после разрыва»
///     over the <c>Subscribe</c> of the fake market-data service.
/// </summary>
public sealed class GrpcMarketFeedTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    private const string WithXrp =
        """
        [
          { "id": "btc", "listing": { "category": "Linear", "symbol": "BTCUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearPerpetual" }, "delistedAt": null },
          { "id": "sol", "listing": { "category": "Linear", "symbol": "SOLUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearPerpetual" }, "delistedAt": null },
          { "id": "xrp", "listing": { "category": "Linear", "symbol": "XRPUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearPerpetual" }, "delistedAt": null }
        ]
        """;

    [Test]
    public async Task A_spot_minute_is_left_out()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        await using var reader = Reader.Start(server, new FakeTimeProvider(Now));
        await reader.Until<MarketPairs>(_ => true);
        await Poll.Until("the subscription of minutes", () => Task.FromResult(server.Data.Minutes.Readers == 1));

        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("spot-btc", Now));
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now));
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("eth", Now.AddMinutes(1)));

        await reader.Until<LiveMinute>(minute => minute.Symbol == "ETHUSDT");
        Assert.That(reader.Minutes.Select(minute => (minute.Symbol, minute.OpenTime)), Is.EqualTo(new[] { ("BTCUSDT", Now), ("ETHUSDT", Now.AddMinutes(1)) }));
    }

    [Test]
    public async Task A_new_pair_is_kept_within_an_hour_and_a_delisted_one_is_dropped()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var time = new FakeTimeProvider(Now);
        await using var reader = Reader.Start(server, time);
        await reader.Until<MarketPairs>(_ => true);
        await Poll.Until("the subscription of minutes", () => Task.FromResult(server.Data.Minutes.Readers == 1));

        server.Data.Catalog = WithXrp;
        time.Advance(TimeSpan.FromHours(1));
        MarketPairs pairs = await reader.Until<MarketPairs>(listed => listed.Pairs.Any(pair => pair.Symbol == "XRPUSDT"));
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("eth", Now));
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("xrp", Now));
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now));

        await reader.Until<LiveMinute>(minute => minute.Symbol == "BTCUSDT");
        Assert.That(pairs.Pairs.Select(pair => pair.Symbol), Is.EqualTo(new[] { "BTCUSDT", "SOLUSDT", "XRPUSDT" }));
        Assert.That(reader.Minutes.Select(minute => minute.Symbol), Is.EqualTo(new[] { "XRPUSDT", "BTCUSDT" }), "ETHUSDT left the catalog");
    }

    [Test]
    public async Task After_a_break_of_ten_minutes_the_minutes_of_the_break_are_caught_up()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        server.Data.FeedTime = new FixedTime(Now.AddMinutes(20));
        await using var reader = Reader.Start(server, new FakeTimeProvider(Now.AddMinutes(20)), since: Now);
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now));
        await reader.Until<LiveMinute>(_ => true);

        for (int minute = 1; minute <= 10; minute++)
        {
            server.Data.Minutes.Store(FakeLiveFeedTests.Minute("btc", Now.AddMinutes(minute)));
        }

        server.Data.Minutes.Drop();
        await reader.Until<LiveMinute>(minute => minute.OpenTime == Now.AddMinutes(10));

        Assert.That(server.Data.Minutes.Subscriptions, Is.EqualTo(new DateTimeOffset?[] { Now, Now.AddMinutes(-5) }), "again from five minutes before the last minute");
        Assert.That(reader.Minutes.Select(minute => minute.OpenTime).Distinct(), Is.EqualTo(Enumerable.Range(0, 11).Select(minute => Now.AddMinutes(minute))));
    }

    [Test]
    public async Task A_subscriber_cut_off_for_reading_slowly_subscribes_again_from_the_last_minute()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        server.Data.FeedTime = new FixedTime(Now);
        await using var reader = Reader.Start(server, new FakeTimeProvider(Now), since: Now.AddMinutes(-30));
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now.AddMinutes(-20)));
        await reader.Until<LiveMinute>(_ => true);

        server.Data.Minutes.Fail(Grpc.Core.StatusCode.ResourceExhausted);
        await Poll.Until("the second subscription", () => Task.FromResult(server.Data.Minutes.Subscriptions.Count == 2));

        Assert.That(server.Data.Minutes.Subscriptions[1], Is.EqualTo(Now.AddMinutes(-25)));
    }

    [Test]
    public async Task A_minute_told_twice_is_one_minute_in_the_book_with_the_new_volume()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        await using var reader = Reader.Start(server, new FakeTimeProvider(Now));
        await reader.Until<MarketPairs>(_ => true);
        await Poll.Until("the subscription of minutes", () => Task.FromResult(server.Data.Minutes.Readers == 1));

        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now.AddMinutes(5), volume: "1"));
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now.AddMinutes(5), volume: "3"));
        await reader.Until<LiveMinute>(minute => minute.Volume == 3);

        var book = new MarketBook(new FakeTimeProvider(Now.AddMinutes(10)), TimeSpan.FromSeconds(15));
        foreach (LiveMinute minute in reader.Minutes)
        {
            book.Apply(minute);
        }

        Assert.That(book.Minutes("BTCUSDT", Now, Now.AddMinutes(10)).Select(minute => (minute.OpenTime, minute.Volume)), Is.EqualTo(new[] { (Now.AddMinutes(5), 3m) }));
    }

    [Test]
    public async Task A_break_of_two_days_is_a_hole_and_the_last_day_is_caught_up()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var time = new FakeTimeProvider(Now);
        server.Data.FeedTime = time;
        await using var reader = Reader.Start(server, time, since: Now.AddMinutes(-10));
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now.AddMinutes(-1)));
        await reader.Until<LiveMinute>(_ => true);

        time.Advance(TimeSpan.FromHours(48));
        server.Data.Minutes.Drop();
        FeedGap gap = await reader.Until<FeedGap>(item => item.Feed == GrpcMarketFeed.Minutes);

        DateTimeOffset caughtUp = Now.AddHours(48) - TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1);
        Assert.That((gap.From, gap.Until), Is.EqualTo((Now.AddMinutes(-6), caughtUp)));
        await Poll.Until("the second subscription", () => Task.FromResult(server.Data.Minutes.Subscriptions.Count == 2));
        Assert.That(server.Data.Minutes.Subscriptions[1], Is.EqualTo(caughtUp));
    }

    /// <summary>
    ///     Reads a subscription of the client into a list in the background.
    /// </summary>
    /// <summary>
    ///     Task 18.7: a candle the feed cannot read is written to the journal and left out, and the feed goes on.
    /// </summary>
    [Test]
    public async Task A_broken_candle_is_written_to_the_journal_and_the_feed_goes_on()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var log = new ListLogger();
        await using var reader = Reader.Start(server, new FakeTimeProvider(Now), logger: log);
        await reader.Until<MarketPairs>(_ => true);
        await Poll.Until("the subscription of minutes", () => Task.FromResult(server.Data.Minutes.Readers == 1));

        CandleUpdate broken = FakeLiveFeedTests.Minute("btc", Now);
        broken.Close = "not a number";
        server.Data.Minutes.Push(broken);
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now.AddMinutes(1)));

        await reader.Until<LiveMinute>(minute => minute.OpenTime == Now.AddMinutes(1));
        Assert.That(reader.Minutes.Select(minute => minute.OpenTime), Is.EqualTo(new[] { Now.AddMinutes(1) }));
        Assert.That(log.Entries.Any(entry => entry.Level == LogLevel.Warning && entry.Message.Contains(GrpcMarketFeed.Minutes)), Is.True, "the broken candle is in the journal");
    }

    /// <summary>
    ///     Task 18.7: a feed the service refuses with <c>INVALID_ARGUMENT</c> again and again subscribes again after the pause
    ///     of a retry each time and tells one hole for the break.
    /// </summary>
    [Test]
    public async Task A_feed_refused_again_and_again_waits_between_the_tries_and_tells_one_hole()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var time = new FakeTimeProvider(Now);
        server.Data.FeedTime = time;
        server.Data.Minutes.Refuse = Grpc.Core.StatusCode.InvalidArgument;
        var settings = new MarketFeedSettings { RetryDelay = TimeSpan.FromSeconds(1) };
        await using var reader = Reader.Start(server, time, since: Now.AddMinutes(-10), settings, new ListLogger());
        await Poll.Until("the first try", () => Task.FromResult(server.Data.Minutes.Subscriptions.Count == 1));

        await Task.Delay(200);
        Assert.That(server.Data.Minutes.Subscriptions, Has.Count.EqualTo(1), "no try again before the pause");
        for (int retry = 2; retry <= 4; retry++)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            int expected = retry;
            await Poll.Until($"try {retry}", () => Task.FromResult(server.Data.Minutes.Subscriptions.Count == expected));
        }

        Assert.That(reader.Gaps.Count(gap => gap.Feed == GrpcMarketFeed.Minutes), Is.EqualTo(1), "one hole for the break");
    }

    private sealed class Reader : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly List<MarketEvent> _events = [];
        private Task _reading = Task.CompletedTask;

        public IReadOnlyList<FeedGap> Gaps
        {
            get
            {
                lock (_events)
                {
                    return [.. _events.OfType<FeedGap>()];
                }
            }
        }

        public IReadOnlyList<LiveMinute> Minutes
        {
            get
            {
                lock (_events)
                {
                    return [.. _events.OfType<LiveMinute>()];
                }
            }
        }

        public static Reader Start(MarketDataServer server, TimeProvider time, DateTimeOffset? since = null, MarketFeedSettings? settings = null, ILogger? logger = null)
        {
            var feed = new GrpcMarketFeed(server.Channel, new InstrumentCatalog(server.Http), time, settings ?? new MarketFeedSettings { RetryDelay = TimeSpan.Zero }, logger);
            var reader = new Reader();
            reader._reading = Task.Run(async () =>
            {
                await foreach (MarketEvent item in feed.Subscribe(new MarketSubscription(since), reader._stop.Token))
                {
                    lock (reader._events)
                    {
                        reader._events.Add(item);
                    }
                }
            });
            return reader;
        }

        public async Task<T> Until<T>(Func<T, bool> found)
            where T : MarketEvent
        {
            T? match = null;
            await Poll.Until(typeof(T).Name, () =>
            {
                lock (_events)
                {
                    match = _events.OfType<T>().LastOrDefault(found);
                }

                return Task.FromResult(match is not null);
            });
            return match!;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await _reading;
            }
            catch (OperationCanceledException)
            {
                // The reading stops with the test.
            }

            _stop.Dispose();
        }
    }
}
