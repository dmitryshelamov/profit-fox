using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     What the rules of an idea take on the minute it is made: the category of the pair, the last price, the low and high of the setup
///     window, ATR(14) of the interval, the zones, the spread of the pair (none in a replay of history, which has no book), k of the stop
///     buffer and the price step of the pair.
/// </summary>
public sealed record IdeaContext(
    MarketCategory Category,
    decimal Price,
    decimal WindowLow,
    decimal WindowHigh,
    decimal Atr,
    IReadOnlyList<Zone> Zones,
    decimal? Spread,
    decimal StopBuffer,
    decimal Tick);