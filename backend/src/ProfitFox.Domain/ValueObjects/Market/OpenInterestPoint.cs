namespace ProfitFox.Domain.ValueObjects.Market;

/// <summary>The open interest of an instrument at the start of a 5 minute step, in coins of the contract as the exchange reports it.</summary>
public sealed record OpenInterestPoint(DateTimeOffset Time, decimal Value);