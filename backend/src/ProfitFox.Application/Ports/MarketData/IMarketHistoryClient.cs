using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.MarketData;

/// <summary>Candle history of an exchange; the core does not depend on which exchange it is.</summary>
public interface IMarketHistoryClient
{
    /// <summary>Candles with open time in <c>[from, to)</c>, page by page from the end of the period back; each page is newest first.</summary>
    IAsyncEnumerable<IReadOnlyList<Candle>> GetCandlePagesAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    /// <summary>
    ///     One page of the exchange, up to 1000 candles with open time in <c>[from, to)</c>, newest first, in a single attempt: the
    ///     caller waits for it. Throws <see cref="ExchangeUnavailableException" /> when the exchange does not answer.
    /// </summary>
    Task<IReadOnlyList<Candle>> GetCandlesOnceAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    /// <summary>The last price of the pair from its ticker, in a single attempt: a person waits for it. Throws <see cref="ExchangeUnavailableException" /> when the exchange does not answer or has no ticker for the pair.</summary>
    Task<decimal> GetLastPriceAsync(Instrument instrument, CancellationToken cancellationToken);

    /// <summary>Whether the exchange lists the symbol in the category; throws <see cref="ExchangeException" /> when the exchange cannot answer.</summary>
    Task<bool> InstrumentExistsAsync(Instrument instrument, CancellationToken cancellationToken);
}