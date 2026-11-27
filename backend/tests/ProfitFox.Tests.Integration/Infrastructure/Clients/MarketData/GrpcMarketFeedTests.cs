using Google.Protobuf.WellKnownTypes;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Application.Services.Market;
using ProfitFox.Domain.ValueObjects.MarketData.Enums;
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

    private static readonly MarketFeedSettings Unwatched = new MarketFeedSettings { RetryDelay = TimeSpan.Zero, FeedSilence = TimeSpan.Zero };

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

    /// <summary>
    ///     Change add-idea-score, task 6.2: a minute of the feed carries its buy volume, and one the feed tells without it has
    ///     none.
    /// </summary>
    [Test]
    public async Task A_minute_carries_its_buy_volume_when_the_feed_tells_it()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        await using var reader = Reader.Start(server, new FakeTimeProvider(Now));
        await reader.Until<MarketPairs>(_ => true);
        await Poll.Until("the subscription of minutes", () => Task.FromResult(server.Data.Minutes.Readers == 1));
        CandleUpdate unknown = FakeLiveFeedTests.Minute("eth", Now);
        unknown.ClearBuyVolume();

        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now));
        server.Data.Minutes.Push(unknown);

        await reader.Until<LiveMinute>(minute => minute.Symbol == "ETHUSDT");
        Assert.That(reader.Minutes.Select(minute => (minute.Symbol, minute.BuyVolume)), Is.EqualTo(new[] { ("BTCUSDT", (decimal?)6m), ("ETHUSDT", (decimal?)null) }));
    }

    /// <summary>
    ///     Spec market-live-feed «Новая пара» and «Пара снята с торгов»; task 18.8 (г): a pair left out of two answers of the
    ///     catalog in a row is dropped, one answer without it keeps it.
    /// </summary>
    [Test]
    public async Task A_new_pair_is_kept_within_an_hour_and_a_delisted_one_is_dropped_after_two_answers_without_it()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var time = new FakeTimeProvider(Now);
        // The clock leaps by hours with no minute told: the watchdog of the minutes (task 24.1, tested below) is off.
        await using var reader = Reader.Start(server, time, settings: Unwatched);
        await reader.Until<MarketPairs>(_ => true);
        await Poll.Until("the subscription of minutes", () => Task.FromResult(server.Data.Minutes.Readers == 1));

        server.Data.Catalog = WithXrp;
        time.Advance(TimeSpan.FromHours(1));
        MarketPairs pairs = await reader.Until<MarketPairs>(listed => listed.Pairs.Any(pair => pair.Symbol == "XRPUSDT"));
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("eth", Now));
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("xrp", Now));
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now));
        await reader.Until<LiveMinute>(minute => minute.Symbol == "BTCUSDT");
        Assert.That(pairs.Pairs.Select(pair => pair.Symbol), Is.EquivalentTo(new[] { "BTCUSDT", "SOLUSDT", "XRPUSDT", "ETHUSDT" }), "ETHUSDT is left out of one answer only");
        Assert.That(reader.Minutes.Select(minute => minute.Symbol), Is.EqualTo(new[] { "ETHUSDT", "XRPUSDT", "BTCUSDT" }));

        time.Advance(TimeSpan.FromHours(1));
        MarketPairs dropped = await reader.Until<MarketPairs>(listed => listed.Pairs.All(pair => pair.Symbol != "ETHUSDT"));
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("eth", Now.AddMinutes(1)));
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now.AddMinutes(1)));
        await reader.Until<LiveMinute>(minute => minute.Symbol == "BTCUSDT" && minute.OpenTime == Now.AddMinutes(1));

        Assert.That(dropped.Pairs.Select(pair => pair.Symbol), Is.EqualTo(new[] { "BTCUSDT", "SOLUSDT", "XRPUSDT" }));
        Assert.That(reader.Minutes.Count(minute => minute.Symbol == "ETHUSDT"), Is.EqualTo(1), "ETHUSDT left the catalog");
    }

    /// <summary>
    ///     Task 18.8 (г): a pair left out of one answer of the catalog and back in the next is never dropped.
    /// </summary>
    [Test]
    public async Task A_pair_left_out_of_one_answer_of_the_catalog_stays()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var time = new FakeTimeProvider(Now);
        await using var reader = Reader.Start(server, time);
        await reader.Until<MarketPairs>(_ => true);

        server.Data.Catalog = WithXrp;
        time.Advance(TimeSpan.FromHours(1));
        await reader.Until<MarketPairs>(listed => listed.Pairs.Any(pair => pair.Symbol == "XRPUSDT"));
        server.Data.Catalog = MarketDataServer.Catalog.Replace("\"sol\"", "\"xrp\"").Replace("SOLUSDT", "XRPUSDT");
        time.Advance(TimeSpan.FromHours(1));
        await reader.Until<MarketPairs>(listed => listed.Pairs.Any(pair => pair.Symbol == "ETHUSDT") && listed.Pairs.Count == 4);

        Assert.That(reader.Events.OfType<MarketPairs>().All(listed => listed.Pairs.Any(pair => pair.Symbol == "ETHUSDT")), Is.True, "ETHUSDT was never dropped");
    }

    /// <summary>
    ///     Change add-instrument-kind-filter, task 2.2: spec market-overview «Вид пары», scenario «Тип сменился» — the same
    ///     pairs with a new Bybit type are told again, with the new kind.
    /// </summary>
    [Test]
    public async Task A_pair_whose_type_changed_is_told_again_with_its_new_kind()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var time = new FakeTimeProvider(Now);
        await using var reader = Reader.Start(server, time, settings: Unwatched);
        MarketPairs first = await reader.Until<MarketPairs>(_ => true);

        server.Data.Catalog = MarketDataServer.Catalog.Replace("\"symbol\": \"SOLUSDT\",", "\"symbol\": \"SOLUSDT\", \"symbolType\": \"Stock\",");
        time.Advance(TimeSpan.FromHours(1));
        MarketPairs second = await reader.Until<MarketPairs>(listed => listed.Pairs.Any(pair => pair is { Symbol: "SOLUSDT", Kind: InstrumentKind.Stock }));

        Assert.That(first.Pairs.Single(pair => pair.Symbol == "SOLUSDT").Kind, Is.EqualTo(InstrumentKind.Crypto));
        Assert.That(second.Pairs.Select(pair => pair.Symbol), Is.EquivalentTo(first.Pairs.Select(pair => pair.Symbol)), "the set of pairs is the same");
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

    /// <summary>
    ///     Task 26.2: a new subscription of minutes after a break is told before the first minute it replays, so the scanner
    ///     takes the replay as a catch-up; the first subscription tells none, and neither does a new subscription of payments —
    ///     the candles are built from the minutes only.
    /// </summary>
    [Test]
    public async Task A_new_subscription_of_minutes_after_a_break_is_told_before_its_replay()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        server.Data.FeedTime = new FixedTime(Now.AddMinutes(20));
        await using var reader = Reader.Start(server, new FakeTimeProvider(Now.AddMinutes(20)), since: Now);
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now));
        await reader.Until<LiveMinute>(_ => true);
        await Poll.Until("the subscription of payments", () => Task.FromResult(server.Data.Payments.Readers == 1));
        Assert.That(reader.Events.OfType<FeedResubscribed>(), Is.Empty, "the first subscription");

        server.Data.Payments.Drop();
        await Poll.Until("the second subscription of payments", () => Task.FromResult(server.Data.Payments.Subscriptions.Count == 2 && server.Data.Payments.Readers == 1));
        server.Data.Payments.Push(new FundingPaymentUpdate { InstrumentId = "btc", Time = Timestamp.FromDateTimeOffset(Now.AddMinutes(8)), FundingRate = "0.0001" });
        await reader.Until<LivePoint>(point => point.Kind == LivePointKind.FundingPayment);
        for (int minute = 1; minute <= 3; minute++)
        {
            server.Data.Minutes.Store(FakeLiveFeedTests.Minute("btc", Now.AddMinutes(minute)));
        }

        server.Data.Minutes.Drop();
        await reader.Until<LiveMinute>(minute => minute.OpenTime == Now.AddMinutes(3));

        MarketEvent[] events = [.. reader.Events.Where(item => item is LiveMinute or FeedResubscribed)];
        Assert.That(reader.Events.OfType<FeedResubscribed>().Select(item => (item.Feed, item.Since)), Is.EqualTo(new[] { (GrpcMarketFeed.Minutes, (DateTimeOffset?)Now.AddMinutes(-5)) }));
        Assert.That(events.Skip(1).First(), Is.InstanceOf<FeedResubscribed>(), "told before the first minute of the replay");
        Assert.That(events.Skip(2).OfType<LiveMinute>().First().OpenTime, Is.EqualTo(Now), "the replay goes from five minutes before the last minute");
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
    ///     Task 18.9: a subscription for the whole day the service holds starts at its limit when it subscribes, however late
    ///     after the scanner started, and tells no hole.
    /// </summary>
    [Test]
    public async Task A_subscription_for_the_whole_day_tells_no_hole()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var time = new FakeTimeProvider(Now.AddSeconds(30));
        server.Data.FeedTime = time;
        await using var reader = Reader.Start(server, time, since: null, wholeDay: true);
        await Poll.Until("the subscriptions", () => Task.FromResult(server.Data.Minutes.Subscriptions.Count == 1 && server.Data.OpenInterest.Subscriptions.Count == 1));
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now));
        await reader.Until<LiveMinute>(_ => true);

        Assert.That(server.Data.Minutes.Subscriptions.Single(), Is.EqualTo(Now.AddSeconds(30) - TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1)));
        Assert.That(reader.Gaps, Is.Empty);
    }

    /// <summary>
    ///     Task 18.12: a slow reader with a small queue is cut off by the service, subscribes again from its last minute and
    ///     gets every minute.
    /// </summary>
    [Test]
    public async Task A_slow_reader_loses_no_minute()
    {
        const int count = 600;
        await using MarketDataServer server = await MarketDataServer.Start();
        var time = new FakeTimeProvider(Now.AddMinutes(count));
        server.Data.FeedTime = time;
        server.Data.Minutes.MaxBacklog = 50;
        var settings = new MarketFeedSettings { RetryDelay = TimeSpan.Zero, QueueCapacity = 20 };
        await using var reader = Reader.Start(server, time, since: Now.AddMinutes(-1), settings, slow: TimeSpan.FromMilliseconds(2));
        await Poll.Until("the subscription of minutes", () => Task.FromResult(server.Data.Minutes.Readers == 1));

        for (int minute = 0; minute < count; minute++)
        {
            server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now.AddMinutes(minute)));
        }

        await reader.Until<LiveMinute>(minute => minute.OpenTime == Now.AddMinutes(count - 1));
        Assert.That(reader.Minutes.Select(minute => minute.OpenTime).Distinct().Order(), Is.EqualTo(Enumerable.Range(0, count).Select(minute => Now.AddMinutes(minute))));
        Assert.That(server.Data.Minutes.Subscriptions, Has.Count.GreaterThan(1), "the service cut the slow reader off, and it subscribed again");
    }

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

    /// <summary>
    ///     Task 24.1, confirmed: a stream of minutes that stops telling without an error — a half-open connection, a service
    ///     that hangs — was read for ever, and the scanner got no minute and no break. Silent for longer than the limit, it is
    ///     broken off with one warning and subscribed again from a little before its last minute, as after a break.
    /// </summary>
    [Test]
    public async Task A_silent_stream_of_minutes_is_broken_off_and_subscribed_again_from_its_last_minute()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var time = new FakeTimeProvider(Now);
        server.Data.FeedTime = time;
        var log = new ListLogger();
        await using var reader = Reader.Start(server, time, since: Now.AddMinutes(-10), logger: log);
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now.AddMinutes(-1)));
        await reader.Until<LiveMinute>(_ => true);

        time.Advance(TimeSpan.FromMinutes(2));
        await Task.Delay(200);
        Assert.That(server.Data.Minutes.Subscriptions, Has.Count.EqualTo(1), "two minutes of silence are within the limit");

        time.Advance(TimeSpan.FromSeconds(61));
        await Poll.Until("the subscription after the silence", () => Task.FromResult(server.Data.Minutes.Subscriptions.Count == 2 && server.Data.Minutes.Readers == 1));
        Assert.That(server.Data.Minutes.Subscriptions[1], Is.EqualTo(Now.AddMinutes(-6)), "again from five minutes before the last minute");
        server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now.AddMinutes(3)));
        await reader.Until<LiveMinute>(minute => minute.OpenTime == Now.AddMinutes(3));

        (LogLevel Level, string Message)[] warnings = [.. Entries(log).Where(entry => entry.Level == LogLevel.Warning)];
        Assert.That(warnings, Has.Length.EqualTo(1), "one warning for the silence");
        Assert.That(warnings[0].Message, Does.Contain(GrpcMarketFeed.Minutes).And.Contain("00:03"));
        Assert.That(reader.Gaps, Is.Empty, "a silence within the day the service catches up is no hole");
    }

    /// <summary>
    ///     Task 24.1: a quiet night — a minute of one pair every minute, the other pairs without trades — is no silence.
    /// </summary>
    [Test]
    public async Task A_quiet_night_does_not_wake_the_watchdog()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var time = new FakeTimeProvider(Now);
        server.Data.FeedTime = time;
        var log = new ListLogger();
        await using var reader = Reader.Start(server, time, since: Now.AddMinutes(-1), logger: log);
        await Poll.Until("the subscription of minutes", () => Task.FromResult(server.Data.Minutes.Readers == 1));

        for (int minute = 0; minute < 10; minute++)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now.AddMinutes(minute)));
            int expected = minute;
            await reader.Until<LiveMinute>(item => item.OpenTime == Now.AddMinutes(expected));
        }

        Assert.That(server.Data.Minutes.Subscriptions, Has.Count.EqualTo(1));
        Assert.That(Entries(log).Where(entry => entry.Level == LogLevel.Warning), Is.Empty);
    }

    /// <summary>
    ///     Task 24.1: the payments come once in hours, the rates every few minutes and a step of open interest may be missed
    ///     live: those feeds are quiet by their nature, and no silence of theirs subscribes them again.
    /// </summary>
    [Test]
    public async Task The_quiet_feeds_of_funding_and_open_interest_are_not_subscribed_again()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var time = new FakeTimeProvider(Now);
        server.Data.FeedTime = time;
        await using var reader = Reader.Start(server, time, since: Now.AddMinutes(-1), logger: new ListLogger());
        await Poll.Until("the subscriptions", () => Task.FromResult(
            server.Data.Minutes.Readers == 1 && server.Data.Payments.Readers == 1 && server.Data.Rates.Readers == 1 && server.Data.OpenInterest.Readers == 1));

        for (int minute = 0; minute < 120; minute += 2)
        {
            time.Advance(TimeSpan.FromMinutes(2));
            server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", Now.AddMinutes(minute)));
            int expected = minute;
            await reader.Until<LiveMinute>(item => item.OpenTime == Now.AddMinutes(expected));
        }

        Assert.That(
            (server.Data.Minutes.Subscriptions.Count, server.Data.Payments.Subscriptions.Count, server.Data.Rates.Subscriptions.Count, server.Data.OpenInterest.Subscriptions.Count),
            Is.EqualTo((1, 1, 1, 1)),
            "two hours without a payment, a rate or a point of open interest");
    }

    /// <summary>
    ///     Task 24.1: while the reader is slow — the catch-up of a day at the start — the bounded queue holds the feed back.
    ///     The stream is not silent then, the reader is: however long the feed waits for the reader, it is not subscribed
    ///     again, and every minute comes.
    /// </summary>
    [Test]
    public async Task A_feed_held_back_by_a_slow_reader_is_not_taken_for_a_silent_one()
    {
        const int count = 50;
        await using MarketDataServer server = await MarketDataServer.Start();
        var time = new FakeTimeProvider(Now.AddMinutes(count));
        server.Data.FeedTime = time;
        for (int minute = 0; minute < count; minute++)
        {
            server.Data.Minutes.Store(FakeLiveFeedTests.Minute("btc", Now.AddMinutes(minute)));
        }

        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = new ListLogger();
        var settings = new MarketFeedSettings { RetryDelay = TimeSpan.Zero, QueueCapacity = 5 };
        await using var reader = Reader.Start(server, time, since: Now, settings, log, hold: hold.Task);
        await Poll.Until("the subscription of minutes", () => Task.FromResult(server.Data.Minutes.Readers == 1));
        await Task.Delay(300);

        time.Advance(TimeSpan.FromMinutes(10));
        await Task.Delay(300);
        hold.TrySetResult();
        await reader.Until<LiveMinute>(minute => minute.OpenTime == Now.AddMinutes(count - 1));

        Assert.That(server.Data.Minutes.Subscriptions, Has.Count.EqualTo(1), "the feed waited for the reader, not for the stream");
        Assert.That(reader.Minutes.Select(minute => minute.OpenTime), Is.EqualTo(Enumerable.Range(0, count).Select(minute => Now.AddMinutes(minute))));
        Assert.That(Entries(log).Where(entry => entry.Level == LogLevel.Warning), Is.Empty);
    }

    private static (LogLevel Level, string Message)[] Entries(ListLogger log)
    {
        lock (log.Entries)
        {
            return [.. log.Entries];
        }
    }

    private sealed class Reader : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly List<MarketEvent> _events = [];
        private Task _reading = Task.CompletedTask;

        public IReadOnlyList<MarketEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return [.. _events];
                }
            }
        }

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

        // <paramref name="hold" />: the reader takes nothing until it completes.
        public static Reader Start(
            MarketDataServer server,
            TimeProvider time,
            DateTimeOffset? since = null,
            MarketFeedSettings? settings = null,
            ILogger? logger = null,
            bool wholeDay = false,
            TimeSpan? slow = null,
            Task? hold = null)
        {
            var feed = new GrpcMarketFeed(server.Channel, new InstrumentCatalog(server.Http), time, settings ?? new MarketFeedSettings { RetryDelay = TimeSpan.Zero }, logger);
            var reader = new Reader();
            reader._reading = Task.Run(async () =>
            {
                await foreach (MarketEvent item in feed.Subscribe(new MarketSubscription(since, WholeDay: wholeDay), reader._stop.Token))
                {
                    if (hold is not null)
                    {
                        await hold.WaitAsync(reader._stop.Token);
                    }

                    if (slow is { } pause)
                    {
                        await Task.Delay(pause, reader._stop.Token);
                    }

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
