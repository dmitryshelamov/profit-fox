using System.Net;

using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges.Bybit;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

public class BybitRetryTests
{
    private static readonly Instrument BtcLinear = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly DateTimeOffset To = DateTimeOffset.Parse("2024-01-01T01:00:00Z");
    private static readonly string OnePage = BybitJson.Kline("2024-01-01T00:00:00Z");

    private FakeTimeProvider _time = null!;
    private StubHttpHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(DateTimeOffset.Parse("2024-06-01T00:00:00Z"));
        _handler = new StubHttpHandler(_time);
    }

    [TearDown]
    public void TearDown()
    {
        _handler.Dispose();
    }

    [Test]
    public async Task Retries_after_too_many_requests()
    {
        _handler.Respond(HttpStatusCode.TooManyRequests).RespondJson(OnePage);

        List<IReadOnlyList<Candle>> pages = await LoadAsync();

        pages.Count.ShouldBe(1);
        _handler.Requests.Count.ShouldBe(2);
    }

    [Test]
    public async Task Retries_after_rate_limit_ret_code()
    {
        _handler.RespondJson(BybitJson.Error(retCode: 10006, "Too many visits!")).RespondJson(OnePage);

        List<IReadOnlyList<Candle>> pages = await LoadAsync();

        pages.Count.ShouldBe(1);
    }

    [Test]
    public async Task Retries_after_timeout()
    {
        _handler.Respond(() => throw new TaskCanceledException("timeout")).RespondJson(OnePage);

        List<IReadOnlyList<Candle>> pages = await LoadAsync();

        pages.Count.ShouldBe(1);
    }

    [Test]
    public async Task Retries_after_connection_failure()
    {
        _handler.Respond(() => throw new HttpRequestException("connection refused")).RespondJson(OnePage);

        List<IReadOnlyList<Candle>> pages = await LoadAsync();

        pages.Count.ShouldBe(1);
        _handler.Requests.Count.ShouldBe(2);
    }

    [Test]
    public async Task Waits_1_2_4_8_16_seconds_between_retries_of_server_errors()
    {
        for (var i = 0; i < 5; i++)
        {
            _handler.Respond(HttpStatusCode.BadGateway);
        }

        _handler.RespondJson(OnePage);
        DateTimeOffset start = _time.GetUtcNow();

        await LoadAsync();

        _handler.RequestTimes.Select(time => (time - start).TotalSeconds).ShouldBe(new[] { 0d, 1, 3, 7, 15, 31 });
    }

    [Test]
    public async Task Fails_when_retries_are_exhausted()
    {
        for (var i = 0; i < 6; i++)
        {
            _handler.Respond(HttpStatusCode.ServiceUnavailable);
        }

        ExchangeUnavailableException error = await Should.ThrowAsync<ExchangeUnavailableException>(LoadAsync);

        error.Message.ShouldContain("503");
        _handler.Requests.Count.ShouldBe(6);
    }

    [Test]
    public async Task Exhausted_timeouts_are_reported_as_timeout()
    {
        for (var i = 0; i < 6; i++)
        {
            _handler.Respond(() => throw new TaskCanceledException("timeout"));
        }

        ExchangeUnavailableException error = await Should.ThrowAsync<ExchangeUnavailableException>(LoadAsync);

        error.Message.ShouldBe("Bybit request failed after 5 retries: Request timed out");
        _handler.Requests.Count.ShouldBe(6);
    }

    [Test]
    public async Task Fails_immediately_on_other_ret_code()
    {
        _handler.RespondJson(BybitJson.Error(retCode: 10001, "params error"));

        ExchangeException error = await Should.ThrowAsync<ExchangeException>(LoadAsync);

        error.ShouldNotBeOfType<ExchangeUnavailableException>();
        error.Message.ShouldBe("params error");
        _handler.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task Fails_immediately_on_other_http_error()
    {
        _handler.Respond(HttpStatusCode.Forbidden);

        await Should.ThrowAsync<ExchangeException>(LoadAsync);
        _handler.Requests.Count.ShouldBe(1);
    }

    // A shutdown cancels the request in flight; the job must see a cancellation, not an exchange failure.
    [Test]
    public async Task Cancellation_during_a_request_is_not_an_exchange_failure()
    {
        using var cancellation = new CancellationTokenSource();
        _handler.Respond(() =>
        {
            cancellation.Cancel();

            throw new TaskCanceledException("cancelled", null, cancellation.Token);
        });
        BybitMarketDataClient client = BybitClients.Create(_handler, _time);

        Exception error = await Should.ThrowAsync<Exception>(() =>
            client.GetCandlePagesAsync(BtcLinear, Interval.OneHour, From, To, cancellation.Token).ToListAsync().AsTask());

        error.ShouldBeAssignableTo<OperationCanceledException>();
        _handler.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task Cancellation_before_a_request_is_not_an_exchange_failure()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _handler.RespondJson(OnePage);
        BybitMarketDataClient client = BybitClients.Create(_handler, _time);

        Exception error = await Should.ThrowAsync<Exception>(() =>
            client.GetCandlePagesAsync(BtcLinear, Interval.OneHour, From, To, cancellation.Token).ToListAsync().AsTask());

        error.ShouldBeAssignableTo<OperationCanceledException>();
    }

    private async Task<List<IReadOnlyList<Candle>>> LoadAsync()
    {
        BybitMarketDataClient client = BybitClients.Create(_handler, _time);
        Task<List<IReadOnlyList<Candle>>> load = client.GetCandlePagesAsync(BtcLinear, Interval.OneHour, From, To, CancellationToken.None).ToListAsync().AsTask();

        // Move fake time forward in whole seconds, giving the client a moment to react after each step.
        while (!load.IsCompleted)
        {
            await Task.Delay(20);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        return await load;
    }
}