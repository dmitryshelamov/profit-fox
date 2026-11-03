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
}