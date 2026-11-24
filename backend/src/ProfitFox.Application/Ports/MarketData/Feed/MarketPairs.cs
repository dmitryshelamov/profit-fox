using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.MarketData.Feed;

/// <summary>
///     The set of pairs the feeds are kept for now (spec market-history «Набор пар»), read again at least once an hour; a pair
///     that left it is no longer told.
/// </summary>
public sealed record MarketPairs(IReadOnlyList<Instrument> Pairs) : MarketEvent;
