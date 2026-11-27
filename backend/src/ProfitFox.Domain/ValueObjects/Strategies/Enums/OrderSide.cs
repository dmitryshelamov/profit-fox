namespace ProfitFox.Domain.ValueObjects.Strategies.Enums;

/// <summary>
///     The side of an order in a preview: an order of a node <c>enter</c> is long or short, a fill of the orders of the other
///     nodes is a buy or a sale.
/// </summary>
public enum OrderSide
{
    Long,
    Short,
    Buy,
    Sell
}
