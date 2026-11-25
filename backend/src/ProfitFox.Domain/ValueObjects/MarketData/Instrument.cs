using ProfitFox.Domain.ValueObjects.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>
///     An instrument of the market-data catalog: its id there, its symbol on the exchange and its kind (change
///     add-instrument-kind-filter, design.md, decision 2). An instrument made without a kind is crypto.
/// </summary>
public sealed record Instrument(string Id, string Symbol, InstrumentKind Kind = InstrumentKind.Crypto);
