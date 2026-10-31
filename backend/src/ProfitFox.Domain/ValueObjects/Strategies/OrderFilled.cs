namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>A fill of an order, possibly partial; <paramref name="IsComplete" /> says the order is filled completely.</summary>
public sealed record OrderFilled(DateTimeOffset At, string OrderId, decimal Quantity, decimal Price, bool IsComplete) : StrategyEvent(At);