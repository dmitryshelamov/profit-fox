namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>The venue refused the order for good; unlike a cancel, nothing is placed in its stead.</summary>
public sealed record OrderRejected(DateTimeOffset At, string OrderId) : StrategyEvent(At);