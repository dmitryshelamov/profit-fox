using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

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

    public void Replayed(Instrument instrument, int days, TimeSpan engine, TimeSpan reads, TimeSpan writes)
    {
        logger.LogInformation(
            "Replayed {Days} days of {Category}/{Symbol}: engine {EngineMs} ms, reads {ReadsMs} ms, writes {WritesMs} ms.",
            days,
            instrument.Category,
            instrument.Symbol,
            (long)engine.TotalMilliseconds,
            (long)reads.TotalMilliseconds,
            (long)writes.TotalMilliseconds);
    }
}