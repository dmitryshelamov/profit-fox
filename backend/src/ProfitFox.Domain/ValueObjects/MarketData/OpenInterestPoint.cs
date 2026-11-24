namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>
///     The open interest of a pair at a time, in coins of its base asset (change add-ideas-and-market).
/// </summary>
public sealed record OpenInterestPoint(DateTimeOffset Time, decimal Value);
