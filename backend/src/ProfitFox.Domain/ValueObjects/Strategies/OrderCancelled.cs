namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>The venue confirmed the cancel; the order is finished even when partly filled.</summary>
public sealed record OrderCancelled(DateTimeOffset At, string OrderId) : StrategyEvent(At);