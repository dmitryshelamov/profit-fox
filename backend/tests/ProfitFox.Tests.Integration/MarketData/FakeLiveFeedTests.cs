using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using ProfitFox.Infrastructure.Clients.MarketData.Protos;

namespace ProfitFox.Tests.Integration.MarketData;

/// <summary>
///     Change add-ideas-and-market, task 5.2: the <c>Subscribe</c> of the four feeds of the fake market-data service, case
///     by case — catch-up from <c>since</c>, a candle told twice, a dropped connection, <c>RESOURCE_EXHAUSTED</c> and the
///     refusal to catch up further than 24 hours.
/// </summary>
public sealed class FakeLiveFeedTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_subscription_with_since_catches_up_the_stored_minutes_and_then_the_live_ones()
    {
        await using MarketDataServer server = await Server();
        server.Data.Minutes.Store(Minute("btc", Now.AddMinutes(-20)));
        server.Data.Minutes.Store(Minute("btc", Now.AddMinutes(-10)));
        server.Data.Minutes.Store(Minute("btc", Now.AddMinutes(-5)));
        var client = new CandleFeed.CandleFeedClient(server.Channel);

        using AsyncServerStreamingCall<CandleUpdate> call = client.Subscribe(new SubscribeRequest { Since = Timestamp.FromDateTimeOffset(Now.AddMinutes(-10)) });
        await call.ResponseHeadersAsync;
        server.Data.Minutes.Push(Minute("btc", Now));

        Assert.That(await Next(call.ResponseStream, 3), Is.EqualTo(new[] { Now.AddMinutes(-10), Now.AddMinutes(-5), Now }));
        Assert.That(server.Data.Minutes.Subscriptions, Is.EqualTo(new DateTimeOffset?[] { Now.AddMinutes(-10) }));
    }

    [Test]
    public async Task A_candle_told_twice_comes_twice_with_its_new_volume()
    {
        await using MarketDataServer server = await Server();
        var client = new CandleFeed.CandleFeedClient(server.Channel);

        using AsyncServerStreamingCall<CandleUpdate> call = client.Subscribe(new SubscribeRequest());
        await call.ResponseHeadersAsync;
        server.Data.Minutes.Push(Minute("btc", Now, volume: "1"));
        server.Data.Minutes.Push(Minute("btc", Now, volume: "2"));

        Assert.That(await call.ResponseStream.MoveNext(CancellationToken.None), Is.True);
        Assert.That(call.ResponseStream.Current.Volume, Is.EqualTo("1"));
        Assert.That(await call.ResponseStream.MoveNext(CancellationToken.None), Is.True);
        Assert.That((call.ResponseStream.Current.OpenTime.ToDateTimeOffset(), call.ResponseStream.Current.Volume), Is.EqualTo((Now, "2")));
    }

    [Test]
    public async Task A_dropped_connection_breaks_the_stream()
    {
        await using MarketDataServer server = await Server();
        var client = new FundingPaymentFeed.FundingPaymentFeedClient(server.Channel);

        using AsyncServerStreamingCall<FundingPaymentUpdate> call = client.Subscribe(new SubscribeFundingPaymentRequest());
        await call.ResponseHeadersAsync;
        // The test server passes the end of a call on only after a message: one payment comes first.
        server.Data.Payments.Push(new FundingPaymentUpdate { InstrumentId = "btc", Time = Timestamp.FromDateTimeOffset(Now), FundingRate = "0.0001" });
        Assert.That(await call.ResponseStream.MoveNext(CancellationToken.None), Is.True);
        server.Data.Payments.Drop();

        RpcException broken = Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token))!;
        Assert.That(broken.StatusCode, Is.Not.EqualTo(StatusCode.OK));
    }

    [TestCase(StatusCode.ResourceExhausted)]
    [TestCase(StatusCode.Unavailable)]
    public async Task A_subscriber_is_cut_off_with_the_status(StatusCode status)
    {
        await using MarketDataServer server = await Server();
        var client = new OpenInterestFeed.OpenInterestFeedClient(server.Channel);

        using AsyncServerStreamingCall<OpenInterestUpdate> call = client.Subscribe(new SubscribeOpenInterestRequest());
        await call.ResponseHeadersAsync;
        Assert.That(server.Data.OpenInterest.Readers, Is.EqualTo(1));
        // The test server passes the end of a call on only after a message: one point comes first.
        server.Data.OpenInterest.Push(new OpenInterestUpdate { InstrumentId = "btc", Time = Timestamp.FromDateTimeOffset(Now), OpenInterest = "24000" });
        Assert.That(await call.ResponseStream.MoveNext(CancellationToken.None), Is.True);
        server.Data.OpenInterest.Fail(status);

        RpcException cut = Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token))!;
        Assert.That(cut.StatusCode, Is.EqualTo(status));
    }

    [Test]
    public async Task A_since_further_back_than_24_hours_is_refused()
    {
        await using MarketDataServer server = await Server();
        var client = new FundingRateFeed.FundingRateFeedClient(server.Channel);

        using AsyncServerStreamingCall<FundingRateUpdate> call = client.Subscribe(new SubscribeFundingRateRequest { Since = Timestamp.FromDateTimeOffset(Now.AddHours(-48)) });

        RpcException refused = Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token))!;
        Assert.That(refused.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public async Task A_forming_candle_goes_only_to_a_subscriber_that_asked_and_is_not_stored()
    {
        await using MarketDataServer server = await Server();
        var client = new CandleFeed.CandleFeedClient(server.Channel);
        using AsyncServerStreamingCall<CandleUpdate> forming = client.Subscribe(new SubscribeRequest { Forming = true });
        await forming.ResponseHeadersAsync;

        CandleUpdate open = Minute("btc", Now);
        open.Closed = false;
        server.Data.Minutes.PushForming(open);
        server.Data.Minutes.Push(Minute("btc", Now.AddMinutes(1)));

        Assert.That(await forming.ResponseStream.MoveNext(CancellationToken.None), Is.True);
        Assert.That(forming.ResponseStream.Current.Closed, Is.False);
        using AsyncServerStreamingCall<CandleUpdate> later = client.Subscribe(new SubscribeRequest { Since = Timestamp.FromDateTimeOffset(Now.AddHours(-1)) });
        Assert.That(await Next(later.ResponseStream, 1), Is.EqualTo(new[] { Now.AddMinutes(1) }), "the forming candle is not stored");
    }

    private static async Task<MarketDataServer> Server()
    {
        MarketDataServer server = await MarketDataServer.Start();
        server.Data.FeedTime = new FixedTime(Now);
        return server;
    }

    private static async Task<DateTimeOffset[]> Next(IAsyncStreamReader<CandleUpdate> stream, int count)
    {
        List<DateTimeOffset> times = [];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (times.Count < count && await stream.MoveNext(timeout.Token))
        {
            times.Add(stream.Current.OpenTime.ToDateTimeOffset());
        }

        return [.. times];
    }

    /// <summary>
    ///     A closed minute of the fake: the usual prices, with <paramref name="volume" />.
    /// </summary>
    public static CandleUpdate Minute(string instrument, DateTimeOffset open, string volume = "12.5", string close = "100.25", Exchange exchange = Exchange.Bybit)
    {
        CandleUpdate minute = FakeCandleFeed.Minute(instrument, open, volume, "6");
        minute.Close = close;
        minute.Exchange = exchange;
        return minute;
    }
}
