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

    [TestCase(InstrumentKind.Crypto, "crypto")]
    [TestCase(InstrumentKind.Stock, "stock")]
    public void A_kind_reads_and_writes_back_by_its_name(InstrumentKind kind, string name)
    {
        // Decision 4: the names of the kinds in the API and in the database.
        Assert.That(InstrumentKinds.Name(kind), Is.EqualTo(name));
        Assert.That(InstrumentKinds.TryParse(name, out InstrumentKind parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(kind));
    }

    [TestCase("spot")]
    [TestCase("linear")]
    [TestCase("Crypto")]
    [TestCase("")]
    [TestCase(null)]
    public void Another_name_is_not_a_kind(string? name)
    {
        Assert.That(InstrumentKinds.TryParse(name, out _), Is.False);
    }

    [Test]
    public void The_names_are_listed_for_a_refusal()
    {
        Assert.That(InstrumentKinds.Listed, Is.EqualTo("crypto, stock"));
    }
}
