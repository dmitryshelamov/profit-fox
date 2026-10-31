using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.MarketData;

public class InstrumentTests
{
    [Test]
    public void Symbol_is_upper_case()
    {
        new Instrument(MarketCategory.Spot, "btcusdt").Symbol.ShouldBe("BTCUSDT");
    }

    [Test]
    public void Same_symbol_in_different_categories_is_a_different_instrument()
    {
        new Instrument(MarketCategory.Spot, "BTCUSDT").ShouldNotBe(new Instrument(MarketCategory.Linear, "BTCUSDT"));
    }
}