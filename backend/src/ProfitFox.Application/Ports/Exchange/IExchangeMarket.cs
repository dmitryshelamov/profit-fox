using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Ports.Exchange;

/// <summary>
///     The public data of the USDT perpetuals of the exchange (change take-idea-trades, design.md, decision 7): the last
///     price, the rules of a pair and the mark prices. The price is that of the exchange, not of the market-data service: the
///     order fills at the price of the exchange now.
/// </summary>
public interface IExchangeMarket
{
    /// <summary>
    ///     The last price of the pair; null when the exchange does not know it.
    /// </summary>
    /// <exception cref="ExchangeUnavailableException">The exchange did not answer.</exception>
    Task<decimal?> PriceAsync(string symbol, CancellationToken cancellationToken);

    /// <summary>
    ///     The rules of the pair; null when the exchange does not know it.
    /// </summary>
    /// <exception cref="ExchangeUnavailableException">The exchange did not answer.</exception>
    Task<PairRules?> RulesAsync(string symbol, CancellationToken cancellationToken);

    /// <summary>
    ///     The mark prices of the pairs the exchange knows.
    /// </summary>
    /// <exception cref="ExchangeUnavailableException">The exchange did not answer.</exception>
    Task<IReadOnlyDictionary<string, decimal>> MarksAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken);
}
