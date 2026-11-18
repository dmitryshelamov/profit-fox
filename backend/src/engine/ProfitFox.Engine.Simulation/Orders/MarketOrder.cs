namespace ProfitFox.Engine.Simulation.Orders;

/// <summary>
///     A market order placed at a whole minute, for an amount of the quote asset or for a quantity of the base asset.
/// </summary>
public sealed record MarketOrder
{
    /// <summary>
    ///     The moment the order is placed, a whole minute (UTC).
    /// </summary>
    public DateTimeOffset PlacedAt { get; }

    /// <summary>
    ///     Buy or sell.
    /// </summary>
    public OrderSide Side { get; }

    /// <summary>
    ///     The amount of the quote asset to trade; null for an order for a quantity.
    /// </summary>
    public decimal? Amount { get; }

    /// <summary>
    ///     The quantity of the base asset to trade; null for an order for an amount.
    /// </summary>
    public decimal? Quantity { get; }

    private MarketOrder(DateTimeOffset placedAt, OrderSide side, decimal? amount, decimal? quantity)
    {
        PlacedAt = placedAt;
        Side = side;
        Amount = amount;
        Quantity = quantity;
    }

    /// <summary>
    ///     An order for an amount of the quote asset; it fills a quantity of amount / fill price.
    /// </summary>
    public static MarketOrder ForAmount(DateTimeOffset placedAt, OrderSide side, decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        return new MarketOrder(placedAt, side, amount, quantity: null);
    }

    /// <summary>
    ///     An order for a quantity of the base asset.
    /// </summary>
    public static MarketOrder ForQuantity(DateTimeOffset placedAt, OrderSide side, decimal quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        return new MarketOrder(placedAt, side, amount: null, quantity);
    }
}