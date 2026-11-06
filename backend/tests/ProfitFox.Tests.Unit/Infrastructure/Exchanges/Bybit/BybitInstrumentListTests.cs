using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

/// <summary>Requirement "Некриптовые пары сканера": the list of instruments tells a non-crypto pair by the symbol type Bybit gives.</summary>
public class BybitInstrumentListTests
{
    // Scenarios "Спотовый xStock" and "Монета зоны Innovation" (adventure and no type on spot).
    [Test]
    public async Task Spot_xstock_is_tradfi_and_other_spot_pairs_are_crypto()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(
            """
            {"retCode":0,"retMsg":"OK","result":{"category":"spot","list":[
            {"symbol":"TSLAXUSDT","baseCoin":"TSLAX","quoteCoin":"USDT","status":"Trading","symbolType":"xstocks"},
            {"symbol":"ADVUSDT","baseCoin":"ADV","quoteCoin":"USDT","status":"Trading","symbolType":"adventure"},
            {"symbol":"BTCUSDT","baseCoin":"BTC","quoteCoin":"USDT","status":"Trading","symbolType":""}]},"retExtInfo":{},"time":0}
            """);

        IReadOnlyList<ListedInstrument> listed = await BybitClients.Create(handler, TimeProvider.System).ListAsync(MarketCategory.Spot, CancellationToken.None);

        listed.ToDictionary(instrument => instrument.Symbol, instrument => instrument.TradFi)
            .ShouldBe(new Dictionary<string, bool> { ["TSLAXUSDT"] = true, ["ADVUSDT"] = false, ["BTCUSDT"] = false });
    }

    // Scenarios "Перпетуал на акцию", "Сырьё, форекс и ETF" and "Монета зоны Innovation".
    [Test]
    public async Task Linear_stock_etf_commodity_and_forex_are_tradfi_and_coins_are_crypto()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(
            """
            {"retCode":0,"retMsg":"OK","result":{"category":"linear","list":[
            {"symbol":"AAPLUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"AAPL","quoteCoin":"USDT","launchTime":"1700000000000","symbolType":"stock"},
            {"symbol":"ARKKUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"ARKK","quoteCoin":"USDT","launchTime":"1700000000000","symbolType":"ETF"},
            {"symbol":"XAUUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"XAU","quoteCoin":"USDT","launchTime":"1700000000000","symbolType":"commodity"},
            {"symbol":"EURUSDUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"EURUSD","quoteCoin":"USDT","launchTime":"1700000000000","symbolType":"forex"},
            {"symbol":"AIOUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"AIO","quoteCoin":"USDT","launchTime":"1700000000000","symbolType":"innovation"},
            {"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","launchTime":"1700000000000","symbolType":""}],
            "nextPageCursor":""},"retExtInfo":{},"time":0}
            """);

        IReadOnlyList<ListedInstrument> listed = await BybitClients.Create(handler, TimeProvider.System).ListAsync(MarketCategory.Linear, CancellationToken.None);

        listed.ToDictionary(instrument => instrument.Symbol, instrument => instrument.TradFi)
            .ShouldBe(new Dictionary<string, bool>
            {
                ["AAPLUSDT"] = true,
                ["ARKKUSDT"] = true,
                ["XAUUSDT"] = true,
                ["EURUSDUSDT"] = true,
                ["AIOUSDT"] = false,
                ["BTCUSDT"] = false
            });
    }
}