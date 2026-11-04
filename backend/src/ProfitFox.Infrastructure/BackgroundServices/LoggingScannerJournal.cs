using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.Market;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>The scanner in the service log.</summary>
internal sealed class LoggingScannerJournal(ILogger<LoggingScannerJournal> logger) : IScannerJournal
{
    public void Took()
    {
        logger.LogInformation("Took the stream of the market.");
    }

    public void Lost()
    {
        logger.LogWarning("Lost the stream of the market; another scanner holds it.");
    }

    public void Failed(string step, Exception exception)
    {
        logger.LogError(exception, "The scanner step {Step} failed; it is tried again.", step);
    }
}