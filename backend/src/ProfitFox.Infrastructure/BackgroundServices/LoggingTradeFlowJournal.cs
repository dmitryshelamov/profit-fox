using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>The collector of the trade flow in the service log.</summary>
internal sealed class LoggingTradeFlowJournal(ILogger<LoggingTradeFlowJournal> logger) : ITradeFlowJournal
{
    public void Took()
    {
        logger.LogInformation("Took the trade flow.");
    }

    public void Lost()
    {
        logger.LogWarning("Lost the trade flow; another collector holds it.");
    }

    public void Failed(string step, Exception exception)
    {
        logger.LogError(exception, "The collector step {Step} failed; it is tried again.", step);
    }

    public void Rebuilt(Instrument instrument, DateTimeOffset day, int trades, TimeSpan elapsed)
    {
        logger.LogInformation(
            "Rebuilt {Day:yyyy-MM-dd} of {Category}/{Symbol} from the archive: {Trades} trades in {ElapsedMs} ms.",
            day,
            instrument.Category,
            instrument.Symbol,
            trades,
            (long)elapsed.TotalMilliseconds);
    }
}