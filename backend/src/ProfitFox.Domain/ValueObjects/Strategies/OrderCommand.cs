namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>What the strategy asks the venue to do with an order.</summary>
public abstract record OrderCommand(string OrderId);