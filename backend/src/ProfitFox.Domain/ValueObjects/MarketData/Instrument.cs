namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>
///     An instrument of the market-data catalog: its id there and its symbol on the exchange.
/// </summary>
public sealed record Instrument(string Id, string Symbol);
