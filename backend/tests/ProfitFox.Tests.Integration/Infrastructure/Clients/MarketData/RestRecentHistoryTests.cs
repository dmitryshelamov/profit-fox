using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Services.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Logging;

namespace ProfitFox.Tests.Integration.Infrastructure.Clients.MarketData;

/// <summary>
///     Change add-ideas-and-market, tasks 5.4 and 18.1: spec market-live-feed «История для прогрева» over the REST of the
///     fake market-data service, which answers as the real one: an aligned window <c>[from, to)</c>, numbers, pages with
///     <c>continueFrom</c>, <c>202</c> while loading and <c>400</c> for a request the service refuses.
/// </summary>
public sealed class RestRecentHistoryTests
{
    private static readonly DateTimeOffset Midnight = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task At_half_past_midnight_the_candles_of_the_day_before_come_up_to_its_end()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var history = new RestRecentHistory(server.Http);

        RecentRead<HistoryCandle> read = await history.Candles("btc", TimeSpan.FromHours(1), Midnight.AddMinutes(30), 200, CancellationToken.None);

        Assert.That(read.Loading, Is.False);
        Assert.That(read.Items, Has.Count.EqualTo(200));
        Assert.That(read.Items[^1].OpenTime, Is.EqualTo(Midnight.AddHours(-1)), "the last candle of the day before, which History does not give yet");
        Assert.That(read.Items.Select(candle => candle.OpenTime), Is.Ordered.And.Unique);
        Assert.That(read.Items[0].Close, Is.EqualTo("100.25"), "a JSON number is read as its text");
        Assert.That(server.Data.RestRequests, Is.EqualTo(new[] { "candles btc 1h" }));
    }

    [Test]
    public async Task The_client_asks_for_an_aligned_window_with_from_and_to_and_without_a_limit()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var history = new RestRecentHistory(server.Http);

        await history.Candles("btc", TimeSpan.FromMinutes(15), Midnight.AddMinutes(37), 4, CancellationToken.None);

        Dictionary<string, string> query = Query(server.Data.RestQueries.Single());
        Assert.That(query.Keys, Is.EquivalentTo(new[] { "interval", "from", "to" }), "the service has no limit and needs from");
        Assert.That(query["interval"], Is.EqualTo("15m"));
        Assert.That(DateTimeOffset.Parse(query["to"]), Is.EqualTo(Midnight.AddMinutes(30)), "the boundary of the interval not later than before");
        Assert.That(DateTimeOffset.Parse(query["from"]), Is.EqualTo(Midnight.AddMinutes(-30)), "to minus the limit of intervals");
    }

    [Test]
    public async Task The_service_refuses_a_request_without_from_or_off_the_boundaries_or_over_a_thousand_candles()
    {
        await using MarketDataServer server = await MarketDataServer.Start();

        HttpStatusCode noFrom = (await server.Http.GetAsync("api/instruments/btc/candles?interval=1h&to=2026-01-20T00:00:00Z")).StatusCode;
        HttpStatusCode withLimit = (await server.Http.GetAsync("api/instruments/btc/candles?interval=1h&to=2026-01-20T00:00:00Z&limit=200")).StatusCode;
        HttpStatusCode offBoundary = (await server.Http.GetAsync("api/instruments/btc/candles?interval=1h&from=2026-01-19T00:30:00Z&to=2026-01-20T00:00:00Z")).StatusCode;
        HttpStatusCode tooMany = (await server.Http.GetAsync("api/instruments/btc/candles?interval=1m&from=2026-01-19T00:00:00Z&to=2026-01-20T00:00:00Z")).StatusCode;
        HttpStatusCode thousand = (await server.Http.GetAsync("api/instruments/btc/candles?interval=1m&from=2026-01-19T07:20:00Z&to=2026-01-20T00:00:00Z")).StatusCode;
        HttpStatusCode noFromInterest = (await server.Http.GetAsync("api/instruments/btc/open-interest?to=2026-01-20T00:00:00Z")).StatusCode;
        HttpStatusCode noFromFunding = (await server.Http.GetAsync("api/instruments/btc/funding-payments?to=2026-01-20T00:00:00Z")).StatusCode;

        Assert.That(
            new[] { noFrom, withLimit, offBoundary, tooMany, thousand, noFromInterest, noFromFunding },
            Is.EqualTo(new[] { HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest }));
    }

    [Test]
    public async Task More_than_a_thousand_candles_are_read_in_windows_of_at_most_a_thousand_oldest_first()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var history = new RestRecentHistory(server.Http);
        var step = TimeSpan.FromMinutes(15);

        RecentRead<HistoryCandle> read = await history.Candles("btc", step, Midnight, 1200, CancellationToken.None);

        Assert.That(read.Loading, Is.False);
        Assert.That(read.Items, Has.Count.EqualTo(1200), "a warm-up deeper than one answer, as 1199 closes of a trend");
        Assert.That(read.Items.Select(candle => candle.OpenTime), Is.Ordered.And.Unique);
        Assert.That(read.Items[0].OpenTime, Is.EqualTo(Midnight - (step * 1200)));
        Assert.That(read.Items[^1].OpenTime, Is.EqualTo(Midnight - step));
        Assert.That(
            server.Data.RestQueries.Select(Query).Select(query => (DateTimeOffset.Parse(query["from"]), DateTimeOffset.Parse(query["to"]))),
            Is.EqualTo(new[] { (Midnight - (step * 1200), Midnight - (step * 200)), (Midnight - (step * 200), Midnight) }),
            "the service refuses a window of more than 1000 candles");
    }

    [Test]
    public async Task The_service_answers_numbers_and_named_lists()
    {
        await using MarketDataServer server = await MarketDataServer.Start();

        using var candles = JsonDocument.Parse(await server.Http.GetStringAsync("api/instruments/btc/candles?interval=1h&from=2026-01-19T00:00:00Z&to=2026-01-19T02:00:00Z"));
        using var interest = JsonDocument.Parse(await server.Http.GetStringAsync("api/instruments/btc/open-interest?from=2026-01-19T00:00:00Z&to=2026-01-19T00:15:00Z"));
        using var funding = JsonDocument.Parse(await server.Http.GetStringAsync("api/instruments/btc/funding-payments?from=2026-01-19T00:00:00Z&to=2026-01-20T00:00:00Z"));

        JsonElement candle = candles.RootElement.GetProperty("candles")[0];
        Assert.That(candles.RootElement.GetProperty("loading").GetBoolean(), Is.False);
        Assert.That(candle.GetProperty("open").ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(candle.GetProperty("volume").ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(interest.RootElement.GetProperty("points").GetArrayLength(), Is.EqualTo(3), "a point every 5 minutes");
        Assert.That(interest.RootElement.GetProperty("points")[0].GetProperty("openInterest").ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(interest.RootElement.GetProperty("continueFrom").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(funding.RootElement.GetProperty("points")[0].GetProperty("fundingRate").ValueKind, Is.EqualTo(JsonValueKind.Number));
    }

    [Test]
    public async Task The_payments_and_the_open_interest_of_the_pair()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var history = new RestRecentHistory(server.Http);

        RecentRead<HistoryPayment> payments = await history.Funding("btc", Midnight.AddDays(-1), Midnight.AddMinutes(30), CancellationToken.None);
        RecentRead<OpenInterestPoint> interest = await history.OpenInterest("btc", TimeSpan.FromHours(1), Midnight, 3, CancellationToken.None);

        Assert.That(payments.Items.Select(payment => payment.Time), Is.EqualTo(new[] { Midnight.AddDays(-1), Midnight.AddHours(-16), Midnight.AddHours(-8), Midnight }));
        Assert.That(payments.Items[1].Rate, Is.EqualTo("0.0006"));
        Assert.That(interest.Items, Is.EqualTo(new[] { new OpenInterestPoint(Midnight.AddHours(-3), 24_000), new OpenInterestPoint(Midnight.AddHours(-2), 24_000), new OpenInterestPoint(Midnight.AddHours(-1), 24_000) }));
        Assert.That(Query(server.Data.RestQueries.Last()).Keys, Is.EquivalentTo(new[] { "from", "to" }), "open interest has no interval and no limit");
    }

    [Test]
    public async Task The_open_interest_of_a_long_window_follows_continueFrom_to_its_end()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var history = new RestRecentHistory(server.Http);

        RecentRead<OpenInterestPoint> interest = await history.OpenInterest("btc", TimeSpan.FromHours(1), Midnight, 200, CancellationToken.None);

        Assert.That(interest.Items, Has.Count.EqualTo(200), "2400 points of 5 minutes in three answers, the last of each hour");
        Assert.That(interest.Items[0].Time, Is.EqualTo(Midnight.AddHours(-200)));
        Assert.That(interest.Items[^1].Time, Is.EqualTo(Midnight.AddHours(-1)));
        Assert.That(server.Data.RestRequests, Is.EqualTo(new[] { "open-interest btc", "open-interest btc", "open-interest btc" }));
    }

    [Test]
    public async Task The_client_reads_numbers_and_strings_and_follows_continueFrom()
    {
        var service = new PagedService(
            """{ "points": [ { "time": "2026-01-19T00:00:00+00:00", "fundingRate": -0.00003097 } ], "coveredFrom": null, "coveredUntil": null, "continueFrom": "2026-01-19T08:00:00+00:00" }""",
            """{ "points": [ { "time": "2026-01-19T08:00:00+00:00", "fundingRate": "0.0001" } ], "coveredFrom": null, "coveredUntil": null, "continueFrom": null }""");
        var history = new RestRecentHistory(new HttpClient(service) { BaseAddress = new Uri("http://market-data/") });

        RecentRead<HistoryPayment> read = await history.Funding("btc", Midnight.AddDays(-1), Midnight, CancellationToken.None);

        Assert.That(read.Items.Select(payment => payment.Rate), Is.EqualTo(new[] { "-0.00003097", "0.0001" }));
        Assert.That(DateTimeOffset.Parse(Query(service.Requests[1])["from"]), Is.EqualTo(Midnight.AddHours(-16)), "the second page starts at continueFrom");
    }

    [Test]
    public async Task A_page_of_500_days_reads_its_open_interest_with_one_History_call()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var clock = new FakeTimeProvider(Midnight.AddHours(12).AddMinutes(34));
        server.Data.Time = clock;
        var history = new RestRecentHistory(server.Http, server.Channel, clock);

        RecentRead<OpenInterestPoint> interest = await history.OpenInterest("btc", TimeSpan.FromDays(1), clock.GetUtcNow(), 500, CancellationToken.None);

        Assert.That(server.Data.InterestRequests, Has.Count.EqualTo(1), "the days before today in one call of History");
        Assert.That(server.Data.RestRequests, Has.Count.LessThanOrEqualTo(2));
        Assert.That(interest.Items.Select(point => point.Time), Is.EqualTo(Enumerable.Range(0, 15).Select(day => FakeMarketData.CoveredFrom.AddDays(day))));
        Assert.That(interest.Items.Select(point => point.Value), Is.All.EqualTo(24_000m));
    }

    [Test]
    public async Task The_open_interest_of_today_comes_from_the_REST_and_the_days_before_from_History()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var clock = new FakeTimeProvider(Midnight.AddHours(3).AddMinutes(12));
        server.Data.Time = clock;
        var history = new RestRecentHistory(server.Http, server.Channel, clock);

        RecentRead<OpenInterestPoint> interest = await history.OpenInterest("btc", TimeSpan.FromHours(1), clock.GetUtcNow(), 6, CancellationToken.None);

        Assert.That(interest.Items.Select(point => point.Time), Is.EqualTo(Enumerable.Range(-3, 6).Select(hour => Midnight.AddHours(hour))), "no hole at midnight");
        Assert.That(server.Data.InterestRequests.Select(request => (request.From.ToDateTimeOffset(), request.To.ToDateTimeOffset())), Is.EqualTo(new[] { (Midnight.AddHours(-3), Midnight) }));
        Assert.That(DateTimeOffset.Parse(Query(server.Data.RestQueries.Single())["from"]), Is.EqualTo(Midnight));
    }

    /// <summary>
    ///     Task 19.7: just after midnight by the clock of the bench, which is ahead of the clock of the service, History refuses
    ///     a window that ends at the start of the day of the bench — the points come from the REST instead.
    /// </summary>
    [Test]
    public async Task A_refusal_of_History_reads_the_open_interest_from_the_REST()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        server.Data.Time = new FakeTimeProvider(Midnight.AddSeconds(-30));
        var bench = new FakeTimeProvider(Midnight.AddSeconds(30));
        var history = new RestRecentHistory(server.Http, server.Channel, bench);

        RecentRead<OpenInterestPoint> interest = await history.OpenInterest("btc", TimeSpan.FromHours(1), bench.GetUtcNow(), 6, CancellationToken.None);

        Assert.That(server.Data.InterestRequests, Has.Count.EqualTo(1), "History was asked and refused");
        Assert.That(interest.Items.Select(point => point.Time), Is.EqualTo(Enumerable.Range(-6, 6).Select(hour => Midnight.AddHours(hour))));
        Assert.That(DateTimeOffset.Parse(Query(server.Data.RestQueries.Single())["from"]), Is.EqualTo(Midnight.AddHours(-6)));
    }

    /// <summary>
    ///     Task 20.7: a refusal of History for a long window — 500 days of 1d — asks History again with the end a day earlier,
    ///     and the REST reads only the tail after it: no hole, and a page or two of the REST, not the whole window.
    /// </summary>
    [Test]
    public async Task A_refused_History_of_a_long_window_is_asked_again_a_day_earlier_and_the_REST_reads_the_tail()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        server.Data.Time = new FakeTimeProvider(Midnight.AddSeconds(-30));
        var bench = new FakeTimeProvider(Midnight.AddSeconds(30));
        var history = new RestRecentHistory(server.Http, server.Channel, bench);

        RecentRead<OpenInterestPoint> interest = await history.OpenInterest("btc", TimeSpan.FromDays(1), bench.GetUtcNow(), 500, CancellationToken.None);

        Assert.That(server.Data.InterestRequests.Select(request => request.To.ToDateTimeOffset()), Is.EqualTo(new[] { Midnight, Midnight.AddDays(-1) }));
        Assert.That(server.Data.RestRequests.Count(request => request.StartsWith("open-interest", StringComparison.Ordinal)), Is.LessThanOrEqualTo(2));
        Assert.That(DateTimeOffset.Parse(Query(server.Data.RestQueries.First())["from"]), Is.EqualTo(Midnight.AddDays(-1)));
        Assert.That(interest.Items.Select(point => point.Time), Is.EqualTo(Enumerable.Range(0, (int)(Midnight - FakeMarketData.CoveredFrom).TotalDays).Select(day => FakeMarketData.CoveredFrom.AddDays(day))));
    }

    /// <summary>
    ///     Task 21.2, the rule of task 20.7 for two refusals changed: History fails twice — the answer says the history loads,
    ///     without points and without a request to the REST, and the page asks again by its own timer (task 20.6); the last
    ///     two days given as the whole window hid the rest of it from the page.
    /// </summary>
    [Test]
    public async Task A_History_that_fails_twice_answers_that_the_open_interest_loads_without_reading_the_REST()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var clock = new FakeTimeProvider(Midnight.AddHours(12).AddMinutes(34));
        server.Data.Time = clock;
        server.Data.Failure = Grpc.Core.StatusCode.Unavailable;
        var history = new RestRecentHistory(server.Http, server.Channel, clock);

        RecentRead<OpenInterestPoint> interest = await history.OpenInterest("btc", TimeSpan.FromDays(1), clock.GetUtcNow(), 500, CancellationToken.None);

        Assert.That(server.Data.InterestRequests, Has.Count.EqualTo(2));
        Assert.That(server.Data.RestRequests.Count(request => request.StartsWith("open-interest", StringComparison.Ordinal)), Is.Zero);
        Assert.That((interest.Loading, interest.Items.Count), Is.EqualTo((true, 0)));
    }

    /// <summary>
    ///     Task 21.2: two refusals of History are a line of the journal of level <c>Warning</c>, no more than once a minute for
    ///     each reason.
    /// </summary>
    [Test]
    public async Task Two_refusals_of_History_are_written_to_the_journal_once_a_minute_for_each_reason()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var clock = new FakeTimeProvider(Midnight.AddHours(12).AddMinutes(34));
        server.Data.Time = clock;
        server.Data.Failure = Grpc.Core.StatusCode.Unavailable;
        var log = new ListLogger();
        var history = new RestRecentHistory(server.Http, server.Channel, clock, log);
        List<string> Warnings()
        {
            return log.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToList();
        }

        await history.OpenInterest("btc", TimeSpan.FromDays(1), clock.GetUtcNow(), 500, CancellationToken.None);
        await history.OpenInterest("eth", TimeSpan.FromDays(1), clock.GetUtcNow(), 500, CancellationToken.None);
        Assert.That(Warnings(), Has.Count.EqualTo(1), "the same reason within a minute");
        Assert.That(Warnings()[0], Does.Contain("btc").And.Contain("Unavailable"));

        server.Data.Failure = Grpc.Core.StatusCode.Internal;
        await history.OpenInterest("btc", TimeSpan.FromDays(1), clock.GetUtcNow(), 500, CancellationToken.None);
        Assert.That(Warnings(), Has.Count.EqualTo(2), "another reason");

        clock.Advance(TimeSpan.FromSeconds(59));
        server.Data.Failure = Grpc.Core.StatusCode.Unavailable;
        await history.OpenInterest("btc", TimeSpan.FromDays(1), clock.GetUtcNow(), 500, CancellationToken.None);
        Assert.That(Warnings(), Has.Count.EqualTo(2));

        clock.Advance(TimeSpan.FromSeconds(1));
        await history.OpenInterest("btc", TimeSpan.FromDays(1), clock.GetUtcNow(), 500, CancellationToken.None);
        Assert.That(Warnings(), Has.Count.EqualTo(3), "a minute later");
    }

    /// <summary>
    ///     Task 23.2: a History of open interest that hangs ends at the limit of the page — the answer is that it loads, the
    ///     second attempt of task 20.7 and the REST are not asked — and the journal says which step of which pair.
    /// </summary>
    [Test]
    public async Task A_History_of_open_interest_that_hangs_answers_that_it_loads_at_the_limit_of_the_page()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var clock = new FakeTimeProvider(Midnight.AddHours(12).AddMinutes(34));
        server.Data.Time = clock;
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Data.BeforeInterest = async (_, cancellation) =>
        {
            asked.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellation);
        };
        var log = new ListLogger();
        var reads = new PageReads(clock, new TypedLogger<PageReads>(log));
        var history = new RestRecentHistory(server.Http, server.Channel, clock);

        Task<RecentRead<OpenInterestPoint>> read = history.OpenInterest("btc", TimeSpan.FromDays(1), clock.GetUtcNow(), 500, CancellationToken.None, reads.Of("BTCUSDT"));
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(PageReads.Limit);
        ReadTimedOutException? outOfTime = Assert.ThrowsAsync<ReadTimedOutException>(async () => await read.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.That(outOfTime!.Step, Is.EqualTo("oi-history"));
        Assert.That(server.Data.InterestRequests, Has.Count.EqualTo(1), "out of time is not a refusal to ask again a day earlier");
        Assert.That(server.Data.RestRequests, Is.Empty);
        Assert.That(log.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message), Has.One.Contains("oi-history").And.Contains("BTCUSDT"));
    }

    /// <summary>
    ///     Task 23.2: the REST of open interest that hangs ends at the limit of the page as well.
    /// </summary>
    [Test]
    public async Task A_REST_of_open_interest_that_hangs_ends_at_the_limit_of_the_page()
    {
        var clock = new FakeTimeProvider(Midnight.AddHours(12).AddMinutes(34));
        var service = new HangingService();
        var log = new ListLogger();
        var reads = new PageReads(clock, new TypedLogger<PageReads>(log));
        var history = new RestRecentHistory(new HttpClient(service) { BaseAddress = new Uri("http://market-data/") }, time: clock);

        Task<RecentRead<OpenInterestPoint>> read = history.OpenInterest("btc", TimeSpan.FromHours(1), clock.GetUtcNow(), 6, CancellationToken.None, reads.Of("BTCUSDT"));
        await service.Asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(PageReads.Limit);
        ReadTimedOutException? outOfTime = Assert.ThrowsAsync<ReadTimedOutException>(async () => await read.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.That(outOfTime!.Step, Is.EqualTo("oi-rest"));
        Assert.That(async () => await service.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10)), Throws.Nothing, "the request to the service is dropped");
        Assert.That(log.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message), Has.One.Contains("oi-rest").And.Contains("BTCUSDT"));
    }

    [Test]
    public async Task A_refusal_tells_what_was_asked_and_the_answer_of_the_service_but_not_the_pair()
    {
        var service = new PagedService("""{ "error": "from is required" }""") { Status = HttpStatusCode.BadRequest };
        var history = new RestRecentHistory(new HttpClient(service) { BaseAddress = new Uri("http://market-data/") });

        HistoryUnavailableException? refused = Assert.ThrowsAsync<HistoryUnavailableException>(
            () => history.Candles("btc-id", TimeSpan.FromHours(1), Midnight, 200, CancellationToken.None));

        Assert.That(refused!.Message, Is.EqualTo("""The REST of the market-data service answered 400 to candles: { "error": "from is required" }"""));
        Assert.That(refused.Reason, Is.EqualTo("The REST of the market-data service answered 400 to candles"), "task 19.4: the reason without the body");
    }

    [Test]
    public async Task A_pair_whose_history_is_loading_is_still_loading()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        server.Data.Loading.Add("sol");
        var history = new RestRecentHistory(server.Http);

        RecentRead<HistoryCandle> read = await history.Candles("sol", TimeSpan.FromMinutes(15), Midnight, 200, CancellationToken.None);

        Assert.That((read.Loading, read.Items.Count), Is.EqualTo((true, 0)));
    }

    private static Dictionary<string, string> Query(string pathAndQuery)
    {
        string query = pathAndQuery[pathAndQuery.IndexOf('?')..];
        return QueryHelpers.ParseQuery(query).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
    }

    // A REST that answers its pages in turn and records the requests.
    // A service that takes the request and never answers, until the request is dropped.
    private sealed class HangingService : HttpMessageHandler
    {
        public TaskCompletionSource Asked { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The answer comes by the limit; the request is dropped as its token is cancelled, maybe a moment after.
        public TaskCompletionSource Dropped { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Dropped.TrySetResult();
                throw;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    // The journal of a type, kept by a list.
    private sealed class TypedLogger<T>(ListLogger inner) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return inner.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return inner.IsEnabled(logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }

    private sealed class PagedService(params string[] pages) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(pages[Requests.Count - 1], Encoding.UTF8, "application/json") });
        }
    }
}
