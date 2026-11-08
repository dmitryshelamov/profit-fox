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

/// <summary>Requirements "Счётчик запросов к T-Invest" and "Остаток лимита T-Invest по заголовкам".</summary>
public class TInvestMetricsTests
{
    private ServiceProvider _services = null!;
    private MockTInvestApi _api = null!;
    private MetricCollector<long> _requests = null!;
    private MetricCollector<int> _limit = null!;
    private MetricCollector<int> _remaining = null!;
    private TInvestCall _call = null!;

    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        IMeterFactory meters = _services.GetRequiredService<IMeterFactory>();
        _api = new MockTInvestApi();
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2024-03-10T12:00:00Z"));
        var settings = new TInvestSettings();
        var metrics = new TInvestMetrics(meters);
        _requests = new MetricCollector<long>(meters, TInvestMetrics.MeterName, "profitfox.tinvest.requests");
        _limit = new MetricCollector<int>(meters, TInvestMetrics.MeterName, "profitfox.tinvest.limit");
        _remaining = new MetricCollector<int>(meters, TInvestMetrics.MeterName, "profitfox.tinvest.limit_remaining");
        _call = new TInvestCall(new Invokers(_api), new TInvestRequestLimiter(settings, time), metrics, new ExchangeResilienceExecutor(time), settings, time);
    }

    [TearDown]
    public void TearDown()
    {
        _requests.Dispose();
        _limit.Dispose();
        _remaining.Dispose();
        _services.Dispose();
    }

    // Scenario "Чтение свечей".
    [Test]
    public async Task Answered_call_counts_once_as_ok()
    {
        _api.On<GetCandlesRequest, GetCandlesResponse>("GetCandles", _ => new GetCandlesResponse());

        await GetCandlesAsync();

        ShouldBeCounted("market_data", "GetCandles", "ok");
    }

    // Scenario "Лимит токена".
    [Test]
    public async Task Resource_exhausted_counts_as_rate_limited()
    {
        _api.Fail("GetCandles", StatusCode.ResourceExhausted, "30042");

        await Should.ThrowAsync<ExchangeRateLimitedException>(() => GetCandlesAsync());

        ShouldBeCounted("market_data", "GetCandles", "rate_limited");
    }

    [Test]
    public async Task Other_failure_counts_as_an_error()
    {
        _api.Fail("GetAccounts", StatusCode.Unavailable);

        await Should.ThrowAsync<ExchangeUnavailableException>(() => _call.OnceAsync("t.token", invoker => new UsersService.UsersServiceClient(invoker).GetAccountsAsync(new GetAccountsRequest()), CancellationToken.None));

        ShouldBeCounted("users", "GetAccounts", "error");
    }

    // Scenario "Остаток группы market_data": the last limit and the lowest remainder of the period, taken by the collection.
    [Test]
    public async Task Limit_headers_give_the_last_limit_and_the_lowest_remainder_of_the_period()
    {
        foreach (string remaining in new[] { "250", "120", "200" })
        {
            _api.Forget("GetCandles").On<GetCandlesRequest, GetCandlesResponse>(
                "GetCandles",
                _ => new GetCandlesResponse(),
                new Metadata { { "x-ratelimit-limit", "300" }, { "x-ratelimit-remaining", remaining } });
            await GetCandlesAsync();
        }

        _limit.RecordObservableInstruments();
        _remaining.RecordObservableInstruments();

        Measurement(_limit).ShouldBe(300);
        Measurement(_remaining).ShouldBe(120);
    }

    [Test]
    public async Task Period_without_limit_headers_gives_no_remainder()
    {
        _api.On<GetCandlesRequest, GetCandlesResponse>("GetCandles", _ => new GetCandlesResponse(), new Metadata { { "x-ratelimit-limit", "300" }, { "x-ratelimit-remaining", "250" } });
        await GetCandlesAsync();
        _remaining.RecordObservableInstruments();
        _remaining.Clear();

        _remaining.RecordObservableInstruments();

        _remaining.GetMeasurementSnapshot().ShouldBeEmpty();
    }

    private Task<GetCandlesResponse> GetCandlesAsync()
    {
        return _call.OnceAsync("t.token", invoker => new MarketDataService.MarketDataServiceClient(invoker).GetCandlesAsync(new GetCandlesRequest()), CancellationToken.None);
    }

    private void ShouldBeCounted(string group, string method, string outcome)
    {
        CollectedMeasurement<long> measurement = _requests.GetMeasurementSnapshot().ShouldHaveSingleItem();
        measurement.Value.ShouldBe(1);
        measurement.Tags["group"].ShouldBe(group);
        measurement.Tags["method"].ShouldBe(method);
        measurement.Tags["outcome"].ShouldBe(outcome);
    }

    private static int Measurement(MetricCollector<int> collector)
    {
        CollectedMeasurement<int> measurement = collector.GetMeasurementSnapshot().ShouldHaveSingleItem();
        measurement.Tags["group"].ShouldBe("market_data");

        return measurement.Value;
    }

    private sealed class Invokers(CallInvoker invoker) : ITInvestCallInvokers
    {
        public CallInvoker Invoker => invoker;
    }
}