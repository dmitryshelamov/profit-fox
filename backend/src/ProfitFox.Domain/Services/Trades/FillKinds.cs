using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Trades;

/// <summary>
///     What a fill of the pair is to the trade that holds the pair, and the reason of a fill that makes the position smaller
///     (change take-idea-trades, design.md, decision 5), by the first rule that holds:
///     <list type="number">
///         <item>a fill of a liquidation is «ликвидирована», one of an auto-deleveraging «закрыта биржей (ADL)»;</item>
///         <item>a payment of funding is a payment: it goes to the outcome, not to the quantity;</item>
///         <item>a fill of an order of the bench of this trade is its entry, or its close with the reason of the order;</item>
///         <item>a fill of the stop of the position is «по стопу», one of its target «по цели»;</item>
///         <item>any other fill is «на бирже» when it makes the position smaller, and is not the entry when it makes it bigger.</item>
///     </list>
/// </summary>
public static class FillKinds
{
    /// <param name="fill">The fill as the exchange told it.</param>
    /// <param name="trade">The trade that holds the pair.</param>
    /// <param name="orders">The orders of the trade.</param>
    public static (FillKind Kind, TradeCloseReason? Reason) Of(ExchangeFill fill, Trade trade, IReadOnlyCollection<TradeOrder> orders)
    {
        switch (fill.Type)
        {
            case ExchangeFillType.Liquidation:
                return (FillKind.Exit, TradeCloseReason.Liquidation);
            case ExchangeFillType.Adl:
                return (FillKind.Exit, TradeCloseReason.Adl);
            case ExchangeFillType.Funding:
                return (FillKind.Funding, null);
        }

        if (TradeOrderId.Parse(fill.OrderLinkId) is { } link && link.TradeId == trade.Id
                                                            && orders.FirstOrDefault(order => order.Number == link.Number) is { } order)
        {
            return order.Purpose == TradeOrderPurpose.Entry ? (FillKind.Entry, null) : (FillKind.Exit, order.Reason ?? TradeCloseReason.Exchange);
        }

        switch (fill.StopType)
        {
            case ExchangeStopType.StopLoss:
                return (FillKind.Exit, TradeCloseReason.Stop);
            case ExchangeStopType.TakeProfit:
                return (FillKind.Exit, TradeCloseReason.Target);
        }

        return fill.Side == trade.Side ? (FillKind.Increase, null) : (FillKind.Exit, TradeCloseReason.Exchange);
    }

    /// <summary>
    ///     The fill as the trade keeps it.
    /// </summary>
    public static TradeFill ToTrade(ExchangeFill fill, Trade trade, IReadOnlyCollection<TradeOrder> orders)
    {
        (FillKind kind, TradeCloseReason? reason) = Of(fill, trade, orders);
        return new TradeFill(fill.ExecId, trade.Id, fill.Side, fill.Price, fill.Quantity, fill.Fee, kind, reason, fill.Time);
    }
}
