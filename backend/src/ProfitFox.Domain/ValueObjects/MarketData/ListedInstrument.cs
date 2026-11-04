using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>
///     An instrument the exchange lists in a market category, with the coins it trades, whether it trades now and whether it is a
///     perpetual contract (never on spot), and since when it trades if the exchange tells (on linear only).
/// </summary>
public sealed record ListedInstrument(
    MarketCategory Category,
    string Symbol,
    string BaseCoin,
    string QuoteCoin,
    bool Trading = false,
    bool Perpetual = false,
    DateTimeOffset? ListedAt = null);