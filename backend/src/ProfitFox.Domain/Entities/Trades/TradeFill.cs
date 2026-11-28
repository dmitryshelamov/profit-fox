using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Entities.Trades;

/// <summary>
///     A fill of the exchange that belongs to a trade (design.md, decision 5): the id of the execution at Bybit — a fill is
///     kept once by it —, the side of the fill, its price, quantity and fee, what it is to the trade and, for a fill that
///     makes the position smaller, the reason, and when it happened. The fee of a payment of funding is what the account paid:
///     a negative one is money received.
/// </summary>
public sealed record TradeFill(
    string ExecId,
    Guid TradeId,
    TradeSide Side,
    decimal Price,
    decimal Quantity,
    decimal Fee,
    FillKind Kind,
    TradeCloseReason? Reason,
    DateTimeOffset Time);
