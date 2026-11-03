using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.LiveTrading;

namespace ProfitFox.Domain.Entities.LiveTrading;

/// <summary>
///     An order of a live bot, numbered within the bot; <see cref="NodePath" /> is the node that placed it, <c>null</c> for the
///     market exit of a closing position. <see cref="Attempts" /> and <see cref="NextAttemptAt" /> pace the sends after a transient
///     refusal of the exchange. The filled quantity, <see cref="FilledValue" /> and <see cref="Fee" /> are the totals of the
///     order as the exchange reported them; what they grow by is a fill of the position.
/// </summary>
public sealed record LiveOrder(
    Guid BotId,
    int Number,
    int PositionNumber,
    string? NodePath,
    OrderSide Side,
    OrderType Type,
    decimal? Price,
    decimal? Quantity,
    decimal? Amount,
    decimal Reserve,
    LiveOrderStatus Status,
    decimal FilledQuantity,
    string? ExchangeOrderId,
    string? RejectReason,
    int Attempts,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    decimal FilledValue = 0m,
    decimal Fee = 0m)
{
    public ClientOrderId ClientId => new ClientOrderId(BotId, Number);

    /// <summary>The order may still be on the exchange or go there: it is not filled, cancelled or rejected.</summary>
    public bool Unfinished => Status is LiveOrderStatus.PendingSubmit or LiveOrderStatus.Active or LiveOrderStatus.PendingCancel or LiveOrderStatus.CancelSent;

    /// <summary>A cancel of the order is saved or sent.</summary>
    public bool Cancelling => Status is LiveOrderStatus.PendingCancel or LiveOrderStatus.CancelSent;

    /// <summary>A send or a cancel of the order is saved and its pause after a transient refusal, if any, is over.</summary>
    public bool DueAt(DateTimeOffset now)
    {
        return Status is LiveOrderStatus.PendingSubmit or LiveOrderStatus.PendingCancel && (NextAttemptAt is null || NextAttemptAt <= now);
    }

    /// <summary>An order saved before it goes to the exchange.</summary>
    public static LiveOrder New(
        Guid botId,
        int number,
        int positionNumber,
        string? nodePath,
        OrderSide side,
        OrderType type,
        decimal? price,
        decimal? quantity,
        decimal? amount,
        decimal reserve,
        DateTimeOffset now)
    {
        return new LiveOrder(botId, number, positionNumber, nodePath, side, type, price, quantity, amount, reserve, LiveOrderStatus.PendingSubmit, 0m, null, null, 0, null, now, now);
    }

    /// <summary>
    ///     The exchange has the order, by the answer to its send or by what it reports of it: an order waiting to be sent is active,
    ///     any other keeps its status. Without <paramref name="exchangeId" /> the id the order has stays.
    /// </summary>
    public LiveOrder Taken(string? exchangeId)
    {
        return this with
        {
            Status = Status == LiveOrderStatus.PendingSubmit ? LiveOrderStatus.Active : Status,
            ExchangeOrderId = exchangeId ?? ExchangeOrderId
        };
    }

    /// <summary>The totals the exchange counted so far; an order the exchange <paramref name="filled" /> is filled.</summary>
    public LiveOrder Grown(decimal filledQuantity, decimal filledValue, decimal fee, bool filled)
    {
        return this with
        {
            FilledQuantity = filledQuantity,
            FilledValue = filledValue,
            Fee = fee,
            Status = filled ? LiveOrderStatus.Filled : Status
        };
    }

    /// <summary>A cancel saved to be sent now, whatever pause a send of the order waited for.</summary>
    public LiveOrder RequestCancel(DateTimeOffset now)
    {
        return this with { Status = LiveOrderStatus.PendingCancel, NextAttemptAt = null, UpdatedAt = now };
    }

    public LiveOrder CancelTaken()
    {
        return this with { Status = LiveOrderStatus.CancelSent };
    }

    /// <summary>A send the exchange did not take is tried again ever more slowly, at most a minute later.</summary>
    public LiveOrder Deferred(DateTimeOffset now)
    {
        return this with { Attempts = Attempts + 1, NextAttemptAt = now + TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Attempts), 60)) };
    }

    /// <summary>The exchange ended the order as filled; an order by amount has no quantity to tell it by.</summary>
    public LiveOrder Completed()
    {
        return this with { Status = LiveOrderStatus.Filled };
    }

    public LiveOrder Cancelled()
    {
        return this with { Status = LiveOrderStatus.Cancelled };
    }

    public LiveOrder Rejected(string reason)
    {
        return this with { Status = LiveOrderStatus.Rejected, RejectReason = reason };
    }
}