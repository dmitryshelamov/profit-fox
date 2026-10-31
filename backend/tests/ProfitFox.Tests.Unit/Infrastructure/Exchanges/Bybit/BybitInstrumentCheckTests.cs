using System.Net;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges.Bybit;
using ProfitFox.Infrastructure.Exchanges.Retry;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

public class BybitInstrumentCheckTests
{
    private static readonly Instrument EthSpot = new Instrument(MarketCategory.Spot, "ETHUSDT");

    [TestCase(MarketCategory.Spot, "spot")]
    [TestCase(MarketCategory.Linear, "linear")]
    public async Task Known_symbol_exists(MarketCategory category, string code)
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(
            $$"""{"retCode":0,"retMsg":"OK","result":{"category":"{{code}}","list":[{"symbol":"ETHUSDT","status":"Trading"}]},"retExtInfo":{},"time":0}""");

        bool exists = await InstrumentExistsAsync(handler, new Instrument(category, "ETHUSDT"));

        exists.ShouldBeTrue();
        BybitRequests.Describe(handler.Requests.Single()).ShouldBe(BybitRequests.Describe($"/v5/market/instruments-info?category={code}&symbol=ETHUSDT"));
    }

    [Test]
    public async Task Unknown_symbol_does_not_exist()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(
            """{"retCode":0,"retMsg":"OK","result":{"category":"spot","list":[]},"retExtInfo":{},"time":0}""");

        bool exists = await InstrumentExistsAsync(handler, EthSpot);

        exists.ShouldBeFalse();
    }

    // Decision 8: the check is a single attempt, so each failure costs exactly one request.
    [Test]
    public async Task Server_error_means_bybit_is_unavailable()
    {
        using StubHttpHandler handler = new StubHttpHandler().Respond(HttpStatusCode.ServiceUnavailable);

        await Should.ThrowAsync<ExchangeException>(() => InstrumentExistsAsync(handler, EthSpot));
        handler.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task Connection_failure_means_bybit_is_unavailable()
    {
        using StubHttpHandler handler = new StubHttpHandler().Respond(() => throw new HttpRequestException("connection refused"));

        await Should.ThrowAsync<ExchangeException>(() => InstrumentExistsAsync(handler, EthSpot));
        handler.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task Timeout_means_bybit_is_unavailable()
    {
        using StubHttpHandler handler = new StubHttpHandler().Respond(() => throw new TaskCanceledException("timeout"));

        await Should.ThrowAsync<ExchangeException>(() => InstrumentExistsAsync(handler, EthSpot));
        handler.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task Non_zero_ret_code_means_bybit_is_unavailable()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(BybitJson.Error(retCode: 10006, "Too many visits!"));

        await Should.ThrowAsync<ExchangeException>(() => InstrumentExistsAsync(handler, EthSpot));
        handler.Requests.Count.ShouldBe(1);
    }

    // Scenario "Биржа недоступна продолжительно".
    [Test]
    public async Task Open_circuit_fails_requests_without_reaching_bybit()
    {
        using var handler = new StubHttpHandler();

        for (var i = 0; i < 10; i++)
        {
            handler.Respond(HttpStatusCode.ServiceUnavailable);
        }

        BybitMarketDataClient client = BybitClients.Create(handler, new ExchangeResilienceExecutor(TimeProvider.System));

        for (var i = 0; i < 10; i++)
        {
            await Should.ThrowAsync<ExchangeException>(() => client.InstrumentExistsAsync(EthSpot, CancellationToken.None));
        }

        await Should.ThrowAsync<ExchangeException>(() => client.InstrumentExistsAsync(EthSpot, CancellationToken.None));
        await Should.ThrowAsync<ExchangeException>(
            client.GetCandlePagesAsync(
                    EthSpot,
                    Interval.OneHour,
                    DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    DateTimeOffset.Parse("2024-01-01T01:00:00Z"),
                    CancellationToken.None)
                .ToListAsync()
                .AsTask());
        handler.Requests.Count.ShouldBe(10);
    }

    private static Task<bool> InstrumentExistsAsync(StubHttpHandler handler, Instrument instrument)
    {
        return BybitClients.Create(handler, TimeProvider.System).InstrumentExistsAsync(instrument, CancellationToken.None);
    }
}