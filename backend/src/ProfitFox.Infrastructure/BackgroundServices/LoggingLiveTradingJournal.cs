using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.LiveTrading;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>Live trading in the service log: what a Worker took, sent and lost, never a key or a secret.</summary>
internal sealed class LoggingLiveTradingJournal(ILogger<LoggingLiveTradingJournal> logger) : ILiveTradingJournal
{
    public void Took(Guid botId, long epoch)
    {
        logger.LogInformation("Took live bot {BotId}, lease epoch {Epoch}.", botId, epoch);
    }

    public void Lost(Guid botId)
    {
        logger.LogWarning("Lost the lease of live bot {BotId}; another Worker keeps it.", botId);
    }

    public void Sent(Guid botId, string clientId)
    {
        logger.LogInformation("The exchange took order {ClientId} of live bot {BotId}.", clientId, botId);
    }

    public void Refused(Guid botId, string clientId, string reason)
    {
        logger.LogWarning("The exchange refused order {ClientId} of live bot {BotId}: {Reason}", clientId, botId, reason);
    }

    public void Failed(Guid botId, string reason)
    {
        logger.LogWarning("Live bot {BotId} stops: {Reason}", botId, reason);
    }
}