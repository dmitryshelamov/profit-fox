using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.Market;

/// <summary>Writes what the collector of the trade flow did to the service log.</summary>
public interface ITradeFlowJournal
{
    void Took();

    void Lost();

    /// <summary>A step of the collector failed; the collector goes on and tries it again.</summary>
    void Failed(string step, Exception exception);

    /// <summary>A day of a pair was rebuilt from the daily archive: the pair, the day, the trades of the file and how long it took.</summary>
    void Rebuilt(Instrument instrument, DateTimeOffset day, int trades, TimeSpan elapsed);
}