using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>
///     The position built from fills: an entry adds quantity and invested amount, an exit takes them away at the average price.
///     A fee in the coin comes off the quantity; a fee in the quote asset adds to the invested amount of a long and takes from it of a
///     short, so the average price includes the entry fees.
/// </summary>
internal sealed class Account(StrategyDirection direction)
{
    public decimal Quantity { get; private set; }

    public decimal Invested { get; private set; }

    public decimal? LastEntryPrice { get; private set; }

    public DateTimeOffset? LastEntryAt { get; private set; }

    public decimal? AveragePrice => Quantity > 0m ? Invested / Quantity : null;

    public void Enter(decimal quantity, decimal price, decimal fee, FeeAsset asset, DateTimeOffset at)
    {
        decimal quoteFee = asset == FeeAsset.Quote ? fee : 0m;
        Quantity += asset == FeeAsset.Base ? quantity - fee : quantity;
        Invested += quantity * price + (direction == StrategyDirection.Long ? quoteFee : -quoteFee);
        LastEntryPrice = price;
        LastEntryAt = at;
    }

    /// <summary>
    ///     An exit never takes more than the position holds: two exit orders may both fill when one of them was being
    ///     cancelled, and the part beyond the position is not a position of this strategy.
    /// </summary>
    public void Exit(decimal quantity)
    {
        decimal exited = Math.Min(quantity, Quantity);

        if (exited == 0m)
        {
            return;
        }

        Invested -= AveragePrice!.Value * exited;
        Quantity -= exited;

        if (Quantity == 0m)
        {
            Invested = 0m;
            LastEntryPrice = null;
            LastEntryAt = null;
        }
    }
}