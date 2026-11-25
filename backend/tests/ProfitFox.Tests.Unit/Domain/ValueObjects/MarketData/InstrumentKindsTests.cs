using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData.Enums;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.MarketData;

/// <summary>
///     Change add-instrument-kind-filter, task 2.1: the kind of a pair from its Bybit type (spec market-overview «Вид пары»,
///     design.md, decision 1).
/// </summary>
public sealed class InstrumentKindsTests
{
    [Test]
    public void A_contract_on_a_share_is_a_stock()
    {
        // Scenario «Контракт на акцию»: TSLAUSDT with the Bybit type «stock».
        Assert.That(InstrumentKinds.Of("Stock"), Is.EqualTo(InstrumentKind.Stock));
    }

    [TestCase("Etf")]
    [TestCase("Commodity")]
    [TestCase("Forex")]
    public void A_fund_a_commodity_and_a_currency_are_stocks(string symbolType)
    {
        // Scenario «Фонд, сырьё и валюта».
        Assert.That(InstrumentKinds.Of(symbolType), Is.EqualTo(InstrumentKind.Stock));
    }

    [Test]
    public void A_tokenized_share_is_a_stock()
    {
        Assert.That(InstrumentKinds.Of("XStocks"), Is.EqualTo(InstrumentKind.Stock));
    }

    [Test]
    public void A_coin_of_the_innovation_zone_is_crypto()
    {
        // Scenario «Монета зоны инноваций».
        Assert.That(InstrumentKinds.Of("Innovation"), Is.EqualTo(InstrumentKind.Crypto));
        Assert.That(InstrumentKinds.Of("Adventure"), Is.EqualTo(InstrumentKind.Crypto));
    }

    [TestCase(null)]
    [TestCase("")]
    public void A_pair_without_a_type_is_crypto(string? symbolType)
    {
        // Scenario «Типа нет»: BTCUSDT while the market-data service gives no type yet.
        Assert.That(InstrumentKinds.Of(symbolType), Is.EqualTo(InstrumentKind.Crypto));
    }

    [TestCase("ETF")]
    [TestCase("Etf")]
    [TestCase("etf")]
    [TestCase("stock")]
    [TestCase("FOREX")]
    [TestCase("xstocks")]
    public void The_type_is_read_without_regard_to_case(string symbolType)
    {
        // Bybit writes «ETF», its SDK «Etf».
        Assert.That(InstrumentKinds.Of(symbolType), Is.EqualTo(InstrumentKind.Stock));
    }

    [TestCase("Bond")]
    [TestCase("spot")]
    [TestCase("Stocks")]
    public void An_unknown_type_is_crypto(string symbolType)
    {
        Assert.That(InstrumentKinds.Of(symbolType), Is.EqualTo(InstrumentKind.Crypto));
    }
}
