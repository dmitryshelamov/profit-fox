namespace ProfitFox.Application.Contracts.MarketData;

/// <summary>
///     A pair of the bench: its id in the market-data catalog and its symbol on the exchange.
/// </summary>
public sealed record InstrumentInternal(string Id, string Symbol);
