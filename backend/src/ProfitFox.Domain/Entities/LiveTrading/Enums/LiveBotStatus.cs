namespace ProfitFox.Domain.Entities.LiveTrading.Enums;

/// <summary>Where a bot is in live trading.</summary>
public enum LiveBotStatus
{
    Stopped,
    Running,
    Paused,

    /// <summary>The active orders of the bot are being cancelled; then it is stopped, or failed when it has a reason.</summary>
    Stopping,

    /// <summary>Stopped by a failure, whose reason the bot keeps.</summary>
    Failed
}