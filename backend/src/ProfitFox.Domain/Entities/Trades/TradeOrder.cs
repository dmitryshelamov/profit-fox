using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Services.Trades;

namespace ProfitFox.Domain.Entities.Trades;

/// <summary>
///     An order of the bench (design.md, decision 4): written before it is sent, with the number that makes its link id at the
///     exchange, so a repeat of the send carries the same one; its purpose and, for a close, the reason; its quantity; where it
///     is; how many sends were tried and when the next may go; the id the exchange gave it; and why the exchange refused it.
/// </summary>
public sealed record TradeOrder(
    Guid TradeId,
    int Number,
    TradeOrderPurpose Purpose,
    TradeCloseReason? Reason,
    decimal Quantity,
    TradeOrderStatus Status,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    string? ExchangeOrderId,
    string? Refusal,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    ///     The <c>orderLinkId</c> of the order at Bybit.
    /// </summary>
    public string LinkId => TradeOrderId.Format(TradeId, Number);

    /// <summary>
    ///     Whether the order may move to <paramref name="status" />: only forward, and an end is final.
    /// </summary>
    public bool CanMoveTo(TradeOrderStatus status)
    {
        return Status switch
        {
            TradeOrderStatus.Pending => status != TradeOrderStatus.Pending,
            TradeOrderStatus.Sent => status is TradeOrderStatus.Filled or TradeOrderStatus.Rejected or TradeOrderStatus.Cancelled,
            _ => false
        };
    }
}
