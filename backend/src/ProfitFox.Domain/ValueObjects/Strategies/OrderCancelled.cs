namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>The venue confirmed a cancel the bot asked for; the order is finished even when partly filled.</summary>
public sealed record OrderCancelled(DateTimeOffset At, string OrderId) : StrategyEvent(At);