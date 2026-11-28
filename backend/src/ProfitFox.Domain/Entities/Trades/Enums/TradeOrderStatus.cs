namespace ProfitFox.Domain.Entities.Trades.Enums;

/// <summary>
///     Where an order of the bench is (design.md, decision 4): written and not sent, taken by the exchange, filled,
///     refused, or given up. It only goes forward: from <see cref="Pending" /> to <see cref="Sent" />, and from either to
///     one of the three ends.
/// </summary>
public enum TradeOrderStatus
{
    Pending,
    Sent,
    Filled,
    Rejected,
    Cancelled
}
