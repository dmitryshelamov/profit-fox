using System.Diagnostics.Metrics;

using Grpc.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Exceptions;
using ProfitFox.Infrastructure.Exchanges.Retry;
using ProfitFox.Infrastructure.Exchanges.TInvest;
using ProfitFox.Tests.Shared.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Decision 7: limiter → metadata → deadline → metrics → the exceptions of Application.</summary>
public class TInvestCallTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private ServiceProvider _services = null!;
    private MockTInvestApi _api = null!;
    private FakeTimeProvider _time = null!;
    private TInvestRequestLimiter _limiter = null!;
    private MetricCollector<long> _requests = null!;
    private TInvestCall _call = null!;

    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _api = new MockTInvestApi();
        _time = new FakeTimeProvider(Start);
        var settings = new TInvestSettings();
        _limiter = new TInvestRequestLimiter(settings, _time);
        var metrics = new TInvestMetrics(_services.GetRequiredService<IMeterFactory>());
        _requests = new MetricCollector<long>(_services.GetRequiredService<IMeterFactory>(), TInvestMetrics.MeterName, "profitfox.tinvest.requests");
        _call = new TInvestCall(new Invokers(_api), _limiter, metrics, new ExchangeResilienceExecutor(_time), settings, _time);
    }

    [TearDown]
    public void TearDown()
    {
        _requests.Dispose();
        _services.Dispose();
    }

    [Test]
    public async Task Call_carries_the_token_the_app_name_and_the_deadline()
    {
        _api.On<GetAccountsRequest, GetAccountsResponse>("GetAccounts", _ => new GetAccountsResponse { Accounts = { new Account { Id = "2000000001" } } });

        GetAccountsResponse response = await _call.OnceAsync("t.token", invoker => new UsersService.UsersServiceClient(invoker).GetAccountsAsync(new GetAccountsRequest()), CancellationToken.None);

        response.Accounts.Single().Id.ShouldBe("2000000001");
        RecordedCall call = _api.Calls.ShouldHaveSingleItem();
        call.Header("authorization").ShouldBe("Bearer t.token");
        call.Header("x-app-name").ShouldBe("profit-fox");
        call.Deadline.ShouldBe((Start + TimeSpan.FromSeconds(10)).UtcDateTime);
    }

    [TestCase(StatusCode.Unauthenticated)]
    [TestCase(StatusCode.PermissionDenied)]
    public async Task Rejected_token_is_a_rejected_key_without_the_text_of_the_broker(StatusCode status)
    {
        _api.Fail("GetAccounts", status, "40003", new Metadata { { "message", "Token is invalid or expired" } });

        ExchangeKeyRejectedException exception = await Should.ThrowAsync<ExchangeKeyRejectedException>(() => GetAccountsAsync());

        exception.Message.ShouldBe("T-Invest does not accept the token.");
    }

    // The group of the token pauses until the reset the server named; a call of the group waits for it and is not counted meanwhile.
    [Test]
    public async Task Resource_exhausted_pauses_the_group_until_the_reset()
    {
        _api.Fail("GetCandles", StatusCode.ResourceExhausted, "30042", new Metadata { { "x-ratelimit-reset", "7" } });

        await Should.ThrowAsync<ExchangeRateLimitedException>(() => GetCandlesAsync());

        _api.Forget("GetCandles").On<GetCandlesRequest, GetCandlesResponse>("GetCandles", _ => new GetCandlesResponse());
        Task<GetCandlesResponse> held = GetCandlesAsync();
        _time.Advance(TimeSpan.FromSeconds(7) - TimeSpan.FromTicks(1));
        held.IsCompleted.ShouldBeFalse();
        _requests.GetMeasurementSnapshot().Sum(measurement => measurement.Value).ShouldBe(1);
        _time.Advance(TimeSpan.FromTicks(1));
        await held.WaitAsync(TimeSpan.FromSeconds(5));
        _api.Calls.Count.ShouldBe(2);
    }

    [TestCase("0")]
    [TestCase("3601")]
    [TestCase("soon")]
    public async Task Reset_outside_one_second_to_an_hour_pauses_for_a_minute(string reset)
    {
        _api.Fail("GetCandles", StatusCode.ResourceExhausted, "30042", new Metadata { { "x-ratelimit-reset", reset } });
        await Should.ThrowAsync<ExchangeRateLimitedException>(() => GetCandlesAsync());
        _api.Forget("GetCandles").On<GetCandlesRequest, GetCandlesResponse>("GetCandles", _ => new GetCandlesResponse());

        Task<GetCandlesResponse> held = GetCandlesAsync();
        _time.Advance(TimeSpan.FromSeconds(59));
        held.IsCompleted.ShouldBeFalse();
        _time.Advance(TimeSpan.FromSeconds(1));

        await held.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Pause_of_a_group_does_not_hold_the_other_groups()
    {
        _api.Fail("GetCandles", StatusCode.ResourceExhausted, "30042", new Metadata { { "x-ratelimit-reset", "7" } });
        _api.On<GetAccountsRequest, GetAccountsResponse>("GetAccounts", _ => new GetAccountsResponse());
        await Should.ThrowAsync<ExchangeRateLimitedException>(() => GetCandlesAsync());

        Task<GetAccountsResponse> other = GetAccountsAsync();

        other.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [TestCase(StatusCode.Unavailable)]
    [TestCase(StatusCode.Internal)]
    [TestCase(StatusCode.Aborted)]
    [TestCase(StatusCode.DeadlineExceeded)]
    public async Task Failure_of_the_server_is_an_unavailable_exchange_in_a_single_attempt(StatusCode status)
    {
        _api.Fail("GetCandles", status);

        await Should.ThrowAsync<ExchangeUnavailableException>(() => GetCandlesAsync());

        _api.Calls.Count.ShouldBe(1);
    }

    [Test]
    public async Task Retries_repeat_a_failure_of_the_server_by_the_ladder()
    {
        _api.Fail("GetCandles", StatusCode.Unavailable);

        Task<GetCandlesResponse> retried = _call.WithRetriesAsync("t.token", invoker => new MarketDataService.MarketDataServiceClient(invoker).GetCandlesAsync(new GetCandlesRequest()), CancellationToken.None);

        foreach (int seconds in new[] { 1, 2, 4, 8, 16 })
        {
            await WaitForCallsAsync(_api.Calls.Count);
            _time.Advance(TimeSpan.FromSeconds(seconds));
        }

        await Should.ThrowAsync<ExchangeUnavailableException>(() => retried);
        _api.Calls.Count.ShouldBe(6);
    }

    [Test]
    public async Task Refusal_with_a_numeric_detail_carries_the_code_and_no_text_of_the_broker()
    {
        _api.Fail("GetCandles", StatusCode.InvalidArgument, "30220", new Metadata { { "message", "Параметр limit не поддерживается" } });

        ExchangeException exception = await Should.ThrowAsync<ExchangeException>(() => GetCandlesAsync());

        exception.Message.ShouldBe("T-Invest refused the request with code 30220.");
    }

    [Test]
    public async Task Refusal_without_a_numeric_detail_names_the_status_only()
    {
        _api.Fail("GetCandles", StatusCode.FailedPrecondition, "some broker text");

        ExchangeException exception = await Should.ThrowAsync<ExchangeException>(() => GetCandlesAsync());

        exception.Message.ShouldBe("T-Invest refused the request (FailedPrecondition).");
    }

    // After the review: a call cancelled by its own token is no failure of the exchange; it is not counted and not translated.
    [Test]
    public async Task Cancelled_call_is_an_operation_cancelled_and_not_counted()
    {
        using var cancellation = new CancellationTokenSource();
        _api.On<GetAccountsRequest, GetAccountsResponse>(
            "GetAccounts",
            _ =>
            {
                cancellation.Cancel();

                throw new RpcException(new Status(StatusCode.Cancelled, "Call canceled by the client."));
            });

        await Should.ThrowAsync<OperationCanceledException>(() => _call.OnceAsync("t.token", invoker => new UsersService.UsersServiceClient(invoker).GetAccountsAsync(new GetAccountsRequest()), cancellation.Token));

        _requests.GetMeasurementSnapshot().ShouldBeEmpty();
    }

    [Test]
    public async Task Not_found_is_told_apart()
    {
        _api.Fail("ShareBy", StatusCode.NotFound, "50002");

        await Should.ThrowAsync<TInvestNotFoundException>(() => _call.OnceAsync("t.token", invoker => new InstrumentsService.InstrumentsServiceClient(invoker).ShareByAsync(new InstrumentRequest()), CancellationToken.None));
    }

    private Task<GetCandlesResponse> GetCandlesAsync()
    {
        return _call.OnceAsync("t.token", invoker => new MarketDataService.MarketDataServiceClient(invoker).GetCandlesAsync(new GetCandlesRequest()), CancellationToken.None);
    }

    private Task<GetAccountsResponse> GetAccountsAsync()
    {
        return _call.OnceAsync("t.token", invoker => new UsersService.UsersServiceClient(invoker).GetAccountsAsync(new GetAccountsRequest()), CancellationToken.None);
    }

    private async Task WaitForCallsAsync(int count)
    {
        while (_api.Calls.Count < count)
        {
            await Task.Yield();
        }
    }

    private sealed class Invokers(CallInvoker invoker) : ITInvestCallInvokers
    {
        public CallInvoker Invoker => invoker;
    }
}