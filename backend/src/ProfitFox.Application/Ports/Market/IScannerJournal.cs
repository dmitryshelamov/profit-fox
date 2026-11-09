using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.Market;

/// <summary>Writes what the scanner did to the service log.</summary>
public interface IScannerJournal
{
    void Took();

    void Lost();

    /// <summary>A step of the scanner failed; the scanner goes on and tries it again.</summary>
    void Failed(string step, Exception exception);

    /// <summary>A turn of the replay of history ended: the pair, the days it replayed and the time of the engine, of the reads and of the writes.</summary>
    void Replayed(Instrument instrument, int days, TimeSpan engine, TimeSpan reads, TimeSpan writes);
}