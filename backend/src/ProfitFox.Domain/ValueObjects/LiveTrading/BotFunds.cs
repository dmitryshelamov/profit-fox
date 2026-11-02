namespace ProfitFox.Domain.ValueObjects.LiveTrading;

/// <summary>The balance of a working bot and what its current position has invested of it.</summary>
public sealed record BotFunds(decimal Balance, decimal Invested);