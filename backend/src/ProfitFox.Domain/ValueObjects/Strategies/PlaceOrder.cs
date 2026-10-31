using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <param name="OrderId"></param>
/// <param name="NodePath">The order node that wants the order.</param>
/// <param name="Side"></param>
/// <param name="Type"></param>
/// <param name="Price">Limit price; <c>null</c> for a market order.</param>
/// <param name="Quantity">Quantity of the base asset; <c>null</c> when the order is sized by <paramref name="Amount" />.</param>
/// <param name="Amount">Amount of the quote asset for a market order sized by amount.</param>
public sealed record PlaceOrder(string OrderId, string NodePath, OrderSide Side, OrderType Type, decimal? Price, decimal? Quantity, decimal? Amount)
    : OrderCommand(OrderId);