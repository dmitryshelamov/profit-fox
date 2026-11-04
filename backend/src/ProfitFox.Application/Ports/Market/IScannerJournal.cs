namespace ProfitFox.Application.Ports.Market;

/// <summary>Writes what the scanner did to the service log.</summary>
public interface IScannerJournal
{
    void Took();

    void Lost();

    /// <summary>A step of the scanner failed; the scanner goes on and tries it again.</summary>
    void Failed(string step, Exception exception);
}