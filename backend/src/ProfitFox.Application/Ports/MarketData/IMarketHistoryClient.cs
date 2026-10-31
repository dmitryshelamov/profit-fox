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

    /// <summary>Whether the exchange lists the symbol in the category; throws <see cref="ExchangeException" /> when the exchange cannot answer.</summary>
    Task<bool> InstrumentExistsAsync(Instrument instrument, CancellationToken cancellationToken);
}