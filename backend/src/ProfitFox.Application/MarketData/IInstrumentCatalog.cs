using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.MarketData;

/// <summary>
///     The set of pairs of the bench (spec market-history): linear Bybit perpetual contracts quoted in USDT that are listed
///     now, ordered by symbol. Delisted pairs are not there, so every report warns about survivors.
/// </summary>
public interface IInstrumentCatalog
{
    /// <exception cref="HistoryUnavailableException">The catalog did not answer.</exception>
    Task<IReadOnlyList<Instrument>> LinearUsdtPerpetuals(CancellationToken cancellation);
}
