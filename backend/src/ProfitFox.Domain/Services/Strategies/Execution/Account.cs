using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>
///     The money of the bot and the position built from fills. An entry adds quantity and invested amount, an exit takes them away at
///     the average price. A fee in the coin comes off the quantity; a fee in the quote asset comes off the cash, adds to the invested
///     amount of a long and takes from it of a short, so the average price includes the entry fees. A short holds the value of its
///     entries as collateral, leverage 1.
/// </summary>
internal sealed class Account(StrategyDirection direction, decimal balance)
{
    private decimal _collateral;

    public decimal Cash { get; private set; } = balance;

    public decimal Quantity { get; private set; }

    public decimal Invested { get; private set; }

    public decimal? LastEntryPrice { get; private set; }

    public DateTimeOffset? LastEntryAt { get; private set; }

    public decimal? AveragePrice => Quantity > 0m ? Invested / Quantity : null;

    public void Enter(decimal quantity, decimal price, decimal fee, FeeAsset asset, DateTimeOffset at)
    {
        decimal quoteFee = asset == FeeAsset.Quote ? fee : 0m;
        Cash -= quantity * price + quoteFee;
        _collateral += quantity * price;
        Quantity += asset == FeeAsset.Base ? quantity - fee : quantity;
        Invested += quantity * price + (direction == StrategyDirection.Long ? quoteFee : -quoteFee);
        LastEntryPrice = price;
        LastEntryAt = at;
    }

    /// <summary>
    ///     An exit never takes more than the position holds: two exit orders may both fill when one of them was being
    ///     cancelled, and the part beyond the position is not a position of this strategy.
    /// </summary>
    public void Exit(decimal quantity, decimal price, decimal fee)
    {
        decimal exited = Math.Min(quantity, Quantity);

        if (exited == 0m)
        {
            return;
        }

        decimal collateral = _collateral * exited / Quantity;
        Cash += direction == StrategyDirection.Long ? exited * price - fee : collateral + (collateral - exited * price) - fee;
        _collateral -= collateral;
        Invested -= AveragePrice!.Value * exited;
        Quantity -= exited;

        if (Quantity == 0m)
        {
            Invested = 0m;
            _collateral = 0m;
            LastEntryPrice = null;
            LastEntryAt = null;
        }
    }
}