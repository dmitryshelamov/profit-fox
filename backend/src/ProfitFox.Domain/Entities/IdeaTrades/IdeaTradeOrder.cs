using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Domain.Entities.IdeaTrades;

/// <summary>
///     An order a Worker sends for a trade by an idea, numbered within the trade: its entry, which carries the stop and the target
///     the exchange stands, or the market close of the owner. It moves like <see cref="LiveTrading.LiveOrder" />: saved before it goes
///     to the exchange, paced after a transient refusal, with the totals the exchange reported.
/// </summary>
public sealed record IdeaTradeOrder(
    Guid TradeId,
    int Number,
    IdeaTradeOrderRole Role,
    OrderSide Side,
    OrderType Type,
    TimeInForce TimeInForce,
    decimal? Price,
    decimal Quantity,
    Protection? Protection,
    bool ReduceOnly,
    LiveOrderStatus Status,
    decimal FilledQuantity,
    decimal FilledValue,
    decimal Fee,
    string? ExchangeOrderId,
    string? RejectReason,
    int Attempts,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public IdeaTradeOrderId ClientId => new IdeaTradeOrderId(TradeId, Number);

    /// <summary>The order may still be on the exchange or go there: it is not filled, cancelled or rejected.</summary>
    public bool Unfinished => Status is LiveOrderStatus.PendingSubmit or LiveOrderStatus.Active or LiveOrderStatus.PendingCancel or LiveOrderStatus.CancelSent;

    /// <summary>A cancel of the order is saved or sent.</summary>
    public bool Cancelling => Status is LiveOrderStatus.PendingCancel or LiveOrderStatus.CancelSent;

    /// <summary>A send or a cancel of the order is saved and its pause after a transient refusal, if any, is over.</summary>
    public bool DueAt(DateTimeOffset now)
    {
        return Status is LiveOrderStatus.PendingSubmit or LiveOrderStatus.PendingCancel && (NextAttemptAt is null || NextAttemptAt <= now);
    }

    public static IdeaTradeOrder NewEntry(
        Guid tradeId,
        int number,
        OrderSide side,
        OrderType type,
        TimeInForce timeInForce,
        decimal? price,
        decimal quantity,
        Protection protection,
        DateTimeOffset now)
    {
        return New(tradeId, number, IdeaTradeOrderRole.Entry, side, type, timeInForce, price, quantity, protection, false, now);
    }

    /// <summary>
    ///     The entry of a new trade, number 1, with the stop and the target of its idea: a limit order at the price of the idea for a pullback,
    ///     a market order for the other types on <c>linear</c>, a limit order that fills at once or not at all (IOC) at the current price on the spot.
    /// </summary>
    public static IdeaTradeOrder NewEntryOf(IdeaTrade trade, DateTimeOffset now)
    {
        bool pullback = trade.Type == IdeaType.Pullback;
        bool market = !pullback && trade.Instrument.Category == MarketCategory.Linear;

        return NewEntry(
            trade.Id,
            1,
            trade.Side == IdeaSide.Long ? OrderSide.Buy : OrderSide.Sell,
            market ? OrderType.Market : OrderType.Limit,
            !pullback && !market ? TimeInForce.ImmediateOrCancel : TimeInForce.GoodTilCancelled,
            market ? null : trade.EntryPrice,
            trade.Quantity,
            new Protection(trade.Target, trade.Stop),
            now);
    }

    public static IdeaTradeOrder NewClose(Guid tradeId, int number, OrderSide side, decimal quantity, DateTimeOffset now)
    {
        return New(tradeId, number, IdeaTradeOrderRole.Close, side, OrderType.Market, TimeInForce.GoodTilCancelled, null, quantity, null, true, now);
    }

    /// <summary>The exchange has the order: one waiting to be sent is active, any other keeps its status. Without an id the one the order has stays.</summary>
    public IdeaTradeOrder Taken(string? exchangeId)
    {
        return this with
        {
            Status = Status == LiveOrderStatus.PendingSubmit ? LiveOrderStatus.Active : Status,
            ExchangeOrderId = exchangeId ?? ExchangeOrderId
        };
    }

    /// <summary>The totals the exchange counted so far; an order the exchange <paramref name="filled" /> is filled.</summary>
    public IdeaTradeOrder Grown(decimal filledQuantity, decimal filledValue, decimal fee, bool filled)
    {
        return this with { FilledQuantity = filledQuantity, FilledValue = filledValue, Fee = fee, Status = filled ? LiveOrderStatus.Filled : Status };
    }

    /// <summary>A cancel saved to be sent now, whatever pause a send of the order waited for.</summary>
    public IdeaTradeOrder RequestCancel(DateTimeOffset now)
    {
        return this with { Status = LiveOrderStatus.PendingCancel, NextAttemptAt = null, UpdatedAt = now };
    }

    public IdeaTradeOrder CancelTaken()
    {
        return this with { Status = LiveOrderStatus.CancelSent };
    }

    /// <summary>A send the exchange did not take is tried again ever more slowly, at most a minute later.</summary>
    public IdeaTradeOrder Deferred(DateTimeOffset now)
    {
        return this with { Attempts = Attempts + 1, NextAttemptAt = now + TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Attempts), 60)) };
    }

    public IdeaTradeOrder Completed()
    {
        return this with { Status = LiveOrderStatus.Filled };
    }

    public IdeaTradeOrder Cancelled()
    {
        return this with { Status = LiveOrderStatus.Cancelled };
    }

    public IdeaTradeOrder Rejected(string reason)
    {
        return this with { Status = LiveOrderStatus.Rejected, RejectReason = reason };
    }

    private static IdeaTradeOrder New(
        Guid tradeId,
        int number,
        IdeaTradeOrderRole role,
        OrderSide side,
        OrderType type,
        TimeInForce timeInForce,
        decimal? price,
        decimal quantity,
        Protection? protection,
        bool reduceOnly,
        DateTimeOffset now)
    {
        return new IdeaTradeOrder(tradeId, number, role, side, type, timeInForce, price, quantity, protection, reduceOnly, LiveOrderStatus.PendingSubmit, 0m, 0m, 0m, null, null, 0, null, now, now);
    }
}