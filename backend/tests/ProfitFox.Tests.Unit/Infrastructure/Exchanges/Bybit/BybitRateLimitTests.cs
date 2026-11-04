using System.Globalization;
using System.Net;

using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

/// <summary>Decision 7: Bybit saying the requests went over its limit is told from other transient failures.</summary>
public class BybitRateLimitTests
{
    private static readonly Instrument Btc = new Instrument(MarketCategory.Spot, "BTCUSDT");
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-01-01T00:00:00Z", CultureInfo.InvariantCulture);

    private readonly FakeTimeProvider _time = new FakeTimeProvider(DateTimeOffset.Parse("2024-06-01T00:00:00Z", CultureInfo.InvariantCulture));
    private StubHttpHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHttpHandler(_time);
    }

    [TearDown]
    public void TearDown()
    {
        _handler.Dispose();
    }

    [Test]
    public async Task Too_many_requests_is_a_rate_limit()
    {
        _handler.Respond(HttpStatusCode.TooManyRequests);

        await Should.ThrowAsync<ExchangeRateLimitedException>(LoadOnceAsync);
    }

    [Test]
    public async Task Rate_limit_ret_code_is_a_rate_limit()
    {
        _handler.RespondJson(BybitJson.Error(retCode: 10006, "Too many visits!"));

        await Should.ThrowAsync<ExchangeRateLimitedException>(LoadOnceAsync);
    }

    [Test]
    public async Task Ip_ban_is_a_rate_limit()
    {
        _handler.Respond(HttpStatusCode.Forbidden);

        await Should.ThrowAsync<ExchangeRateLimitedException>(LoadOnceAsync);
    }

    [Test]
    public async Task Server_error_is_no_rate_limit()
    {
        _handler.Respond(HttpStatusCode.BadGateway);

        ExchangeUnavailableException error = await Should.ThrowAsync<ExchangeUnavailableException>(LoadOnceAsync);

        error.ShouldNotBeOfType<ExchangeRateLimitedException>();
    }

    private Task<IReadOnlyList<Candle>> LoadOnceAsync()
    {
        return BybitClients.Create(_handler, _time).GetCandlesOnceAsync(Btc, Interval.OneHour, From, From.AddHours(1), CancellationToken.None);
    }
}