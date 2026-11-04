using System.Diagnostics.Metrics;
using System.Net.Http.Headers;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Infrastructure.Exchanges.Bybit;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Requirement "Остаток лимита аккаунта по заголовкам Bybit": the last limit of an endpoint and its lowest remainder since the
///     previous collection, which takes it.
/// </summary>
public class BybitLimitHeadroomTests
{
    private const string Orders = "/v5/order/create";

    private ServiceProvider _services = null!;

    // The provider owns and disposes the factory.
    private IMeterFactory Meters => _services.GetRequiredService<IMeterFactory>();

    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
    }

    [Test]
    public void Lowest_remainder_of_the_period_and_the_limit()
    {
        var headroom = new BybitLimitHeadroom(Meters);

        headroom.Observe(Orders, Headers("10", "9"));
        headroom.Observe(Orders, Headers("10", "4"));
        headroom.Observe(Orders, Headers("10", "7"));

        Of(headroom.Limits()).ShouldBe([(Orders, 10)]);
        Of(headroom.TakeRemaining()).ShouldBe([(Orders, 4)]);
    }

    [Test]
    public void Next_period_starts_anew()
    {
        var headroom = new BybitLimitHeadroom(Meters);
        headroom.Observe(Orders, Headers("10", "4"));
        headroom.TakeRemaining();

        headroom.Observe(Orders, Headers("10", "9"));

        Of(headroom.TakeRemaining()).ShouldBe([(Orders, 9)]);
    }

    [Test]
    public void Quiet_period_gives_no_remainder()
    {
        var headroom = new BybitLimitHeadroom(Meters);
        headroom.Observe(Orders, Headers("10", "4"));
        headroom.TakeRemaining();

        headroom.TakeRemaining().ShouldBeEmpty();
    }

    [Test]
    public void Limit_stays_after_a_quiet_period()
    {
        var headroom = new BybitLimitHeadroom(Meters);
        headroom.Observe(Orders, Headers("10", "4"));
        headroom.TakeRemaining();

        Of(headroom.Limits()).ShouldBe([(Orders, 10)]);
    }

    // Public data, such as tickers, comes without the headers; a header that is not a number is not Bybit's either.
    [TestCase(null, null)]
    [TestCase("10", "n/a")]
    public void Answer_without_the_headers_changes_nothing(string? limit, string? remaining)
    {
        var headroom = new BybitLimitHeadroom(Meters);

        headroom.Observe("/v5/market/tickers", Headers(limit, remaining));

        headroom.Limits().ShouldBeEmpty();
        headroom.TakeRemaining().ShouldBeEmpty();
    }

    // Not Bybit's form either: which of the values holds is unknown, and the request must not fail because of the metrics.
    [Test]
    public void Repeated_header_changes_nothing()
    {
        var headroom = new BybitLimitHeadroom(Meters);
        HttpResponseHeaders headers = Headers("10", "4");
        headers.Add("X-Bapi-Limit-Status", "9");

        headroom.Observe(Orders, headers);

        headroom.Limits().ShouldBeEmpty();
        headroom.TakeRemaining().ShouldBeEmpty();
    }

    private static HttpResponseHeaders Headers(string? limit, string? remaining)
    {
        HttpResponseHeaders headers = new HttpResponseMessage().Headers;

        if (limit is not null)
        {
            headers.Add("X-Bapi-Limit", limit);
        }

        if (remaining is not null)
        {
            headers.Add("X-Bapi-Limit-Status", remaining);
        }

        return headers;
    }

    private static (object? Endpoint, int Value)[] Of(IEnumerable<Measurement<int>> measurements)
    {
        return [.. measurements.Select(measurement => (measurement.Tags.ToArray().ShouldHaveSingleItem().Value, measurement.Value))];
    }
}