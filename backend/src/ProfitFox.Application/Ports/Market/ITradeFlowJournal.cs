namespace ProfitFox.Application.Ports.Market;

/// <summary>Writes what the collector of the trade flow did to the service log.</summary>
public interface ITradeFlowJournal
{
    void Took();

    void Lost();

    /// <summary>A step of the collector failed; the collector goes on and tries it again.</summary>
    void Failed(string step, Exception exception);
}