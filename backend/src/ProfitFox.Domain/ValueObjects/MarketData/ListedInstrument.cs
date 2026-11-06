using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>
///     An instrument the exchange lists in a market category, with the coins it trades, whether it trades now and whether it is a
///     perpetual contract (never on spot), since when it trades if the exchange tells (on linear only), the step of its price,
///     whether it is a non-crypto (TradFi) instrument: a stock, an ETF, a commodity or forex, and the step and the minimums of the volume
///     and amount of an order.
/// </summary>
public sealed record ListedInstrument(
    MarketCategory Category,
    string Symbol,
    string BaseCoin,
    string QuoteCoin,
    bool Trading = false,
    bool Perpetual = false,
    DateTimeOffset? ListedAt = null,
    decimal? TickSize = null,
    bool TradFi = false,
    decimal? QuantityStep = null,
    decimal? MinQuantity = null,
    decimal? MinAmount = null);