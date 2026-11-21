using ProfitFox.Domain.MarketData;
using ProfitFox.Infrastructure.MarketData;

namespace ProfitFox.Tests.Unit.Bench.MarketData;

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
}
