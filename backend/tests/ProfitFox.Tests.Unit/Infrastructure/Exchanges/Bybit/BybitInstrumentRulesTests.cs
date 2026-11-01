using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

public class BybitInstrumentRulesTests
{
    [Test]
    public async Task Linear_instrument_without_a_minimum_notional_value_has_no_minimum_amount()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(
            """
            {"retCode":0,"retMsg":"OK","result":{"category":"linear","list":[{"symbol":"BTCUSDT","status":"Trading",
            "priceFilter":{"minPrice":"0.1","maxPrice":"1999999","tickSize":"0.5"},
            "lotSizeFilter":{"maxOrderQty":"100","minOrderQty":"0.01","qtyStep":"0.01"}}]},"retExtInfo":{},"time":0}
            """);

        InstrumentRules rules = await BybitClients.Create(handler, TimeProvider.System).GetRulesAsync(new Instrument(MarketCategory.Linear, "BTCUSDT"), CancellationToken.None);

        rules.ShouldBe(new InstrumentRules(0.5m, 0.01m, 0.01m, 0m));
        BybitRequests.Describe(handler.Requests.Single()).ShouldBe(BybitRequests.Describe("/v5/market/instruments-info?category=linear&symbol=BTCUSDT"));
    }
}