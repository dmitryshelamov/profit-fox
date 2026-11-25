using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData.Enums;
using ProfitFox.Infrastructure.Clients.MarketData;

namespace ProfitFox.Tests.Integration.Infrastructure.Clients.MarketData;

/// <summary>
///     Spec market-history: the set of pairs is the listed linear USDT perpetuals of the catalog.
/// </summary>
public sealed class InstrumentCatalogTests
{
    // A shortened answer of GET api/instruments/bybit with one item of each kind the catalog has.
    private const string Answer =
        """
        [
          { "id": "spot-btc", "listing": { "category": "Spot", "symbol": "BTCUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": null }, "delistedAt": null },
          { "id": "linear-btc", "listing": { "category": "Linear", "symbol": "BTCUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearPerpetual" }, "delistedAt": null },
          { "id": "linear-eth", "listing": { "category": "Linear", "symbol": "ETHUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearPerpetual" }, "delistedAt": null },
          { "id": "usdc-btc", "listing": { "category": "Linear", "symbol": "BTCPERP", "quoteAsset": "USDC", "status": "Trading", "contractType": "LinearPerpetual" }, "delistedAt": null },
          { "id": "future-btc", "listing": { "category": "Linear", "symbol": "BTCUSDT-26DEC26", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearFutures" }, "delistedAt": null },
          { "id": "gone", "listing": { "category": "Linear", "symbol": "OLDUSDT", "quoteAsset": "USDT", "status": "Closed", "contractType": "LinearPerpetual" }, "delistedAt": "2026-10-01T00:00:00+00:00" }
        ]
        """;

    [Test]
    public void Only_listed_linear_USDT_perpetuals_are_in_the_set()
    {
        IReadOnlyList<Instrument> instruments = InstrumentCatalog.Parse(Answer);

        Assert.That(instruments, Is.EqualTo(new[] { new Instrument("linear-btc", "BTCUSDT"), new Instrument("linear-eth", "ETHUSDT") }));
    }

    // Change add-instrument-kind-filter, task 2.2: the same perpetuals with the Bybit type of each, one without the field.
    private const string Typed =
        """
        [
          { "id": "linear-tsla", "listing": { "category": "Linear", "symbol": "TSLAUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearPerpetual", "symbolType": "Stock" }, "delistedAt": null },
          { "id": "linear-spy", "listing": { "category": "Linear", "symbol": "SPYUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearPerpetual", "symbolType": "Etf" }, "delistedAt": null },
          { "id": "linear-new", "listing": { "category": "Linear", "symbol": "NEWUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearPerpetual", "symbolType": "Innovation" }, "delistedAt": null },
          { "id": "linear-btc", "listing": { "category": "Linear", "symbol": "BTCUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearPerpetual", "symbolType": null }, "delistedAt": null },
          { "id": "linear-eth", "listing": { "category": "Linear", "symbol": "ETHUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearPerpetual" }, "delistedAt": null },
          { "id": "spot-tsla", "listing": { "category": "Spot", "symbol": "TSLAUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": null, "symbolType": "Stock" }, "delistedAt": null }
        ]
        """;

    [Test]
    public void Each_pair_has_the_kind_of_its_Bybit_type()
    {
        IReadOnlyList<Instrument> instruments = InstrumentCatalog.Parse(Typed);

        Assert.That(instruments, Is.EqualTo(new[]
        {
            new Instrument("linear-btc", "BTCUSDT", InstrumentKind.Crypto),
            new Instrument("linear-eth", "ETHUSDT", InstrumentKind.Crypto),
            new Instrument("linear-new", "NEWUSDT", InstrumentKind.Crypto),
            new Instrument("linear-spy", "SPYUSDT", InstrumentKind.Stock),
            new Instrument("linear-tsla", "TSLAUSDT", InstrumentKind.Stock)
        }));
    }
}
