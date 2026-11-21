using ProfitFox.Application.Contracts.MarketData;
using ProfitFox.Application.Contracts.MarketData.Converters;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Application.Contracts.MarketData.Converters;

/// <summary>
///     A pair of the catalog keeps its id and symbol in what the API shows of it (design match-market-data-structure, decision 4).
/// </summary>
public sealed class InstrumentInternalConverterTests
{
    [Test]
    public void A_pair_keeps_its_id_and_symbol()
    {
        Assert.That(new Instrument("id-1", "BTCUSDT").ToInternal(), Is.EqualTo(new InstrumentInternal("id-1", "BTCUSDT")));
    }
}
