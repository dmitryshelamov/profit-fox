using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.Market;

/// <summary>The daily archive of the public trades of the exchange.</summary>
public interface ITradeArchive
{
    /// <summary>Reads every trade of <paramref name="instrument" /> in the day UTC that starts at <paramref name="day" /> as the file is downloaded.</summary>
    /// <returns><c>false</c> when the archive has no file of the pair for the day.</returns>
    /// <exception cref="Exceptions.ExchangeUnavailableException">The archive did not answer or broke off.</exception>
    Task<bool> ReadDayAsync(Instrument instrument, DateTimeOffset day, Action<TradeEvent> trade, CancellationToken cancellationToken);
}