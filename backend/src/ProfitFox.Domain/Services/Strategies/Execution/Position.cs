using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>
///     The position of the bot built from fills; the cash of the bot is not part of it and changes by what <see cref="Enter" /> and
///     <see cref="Exit" /> return. An entry adds quantity and invested amount, an exit takes them away at the average price. A fee in
///     the coin comes off the quantity; a fee in the quote asset comes off the cash, adds to the invested amount of a long and takes
///     from it of a short, so the average price includes the entry fees. A short holds the value of its entries as collateral,
///     leverage 1.
/// </summary>
/// <param name="number">The number of the position in the run, from 1.</param>
/// <param name="direction"></param>
/// <param name="minQuantity">An exit that leaves less cannot be followed by another one, so the position closes.</param>
internal sealed class Position(int number, StrategyDirection direction, decimal minQuantity)
{
    private bool _closing;

    public int Number { get; } = number;

    /// <summary>What a short holds in the cash it paid for its entries.</summary>
    public decimal Collateral { get; private set; }

    /// <summary>With the remainder the previous position left below the minimum quantity.</summary>
    public decimal Quantity { get; private set; }

    public decimal Invested { get; private set; }

    /// <summary>What the exits gained over the average price, less their fees; the entry fees are in the average price.</summary>
    public decimal RealizedPnl { get; private set; }

    /// <summary>The fees of the fills in the quote asset; a fee in the coin at the price of its fill.</summary>
    public decimal Fees { get; private set; }

    public decimal? LastEntryPrice { get; private set; }

    public DateTimeOffset? LastEntryAt { get; private set; }

    public decimal? AveragePrice => Quantity > 0m ? Invested / Quantity : null;

    /// <summary>Open from the minimum quantity on, so a remainder alone is no position for the strategy.</summary>
    public PositionStatus Status =>
        _closing ? PositionStatus.Closing : Quantity > 0m && Quantity >= minQuantity ? PositionStatus.Open : PositionStatus.Waiting;

    /// <summary>The position that follows this closed one, with the remainder it left, its invested amount and collateral.</summary>
    public Position Next()
    {
        return new Position(Number + 1, direction, minQuantity) { Quantity = Quantity, Invested = Invested, Collateral = Collateral };
    }

    /// <returns>The change of the cash.</returns>
    public decimal Enter(decimal quantity, decimal price, decimal fee, FeeAsset asset, DateTimeOffset at)
    {
        decimal quoteFee = asset == FeeAsset.Quote ? fee : 0m;
        Collateral += quantity * price;
        Quantity += asset == FeeAsset.Base ? quantity - fee : quantity;
        Invested += quantity * price + (direction == StrategyDirection.Long ? quoteFee : -quoteFee);
        Fees += asset == FeeAsset.Base ? fee * price : fee;
        LastEntryPrice = price;
        LastEntryAt = at;

        return -(quantity * price + quoteFee);
    }

    /// <summary>
    ///     An exit never takes more than the position holds: two exit orders may both fill when one of them was being
    ///     cancelled, and the part beyond the position is not a position of this strategy. An exit that leaves less than the minimum
    ///     quantity starts closing the position.
    /// </summary>
    /// <returns>The change of the cash.</returns>
    public decimal Exit(decimal quantity, decimal price, decimal fee)
    {
        decimal exited = Math.Min(quantity, Quantity);

        if (exited == 0m)
        {
            return 0m;
        }

        decimal returned = Collateral * exited / Quantity;
        decimal cash = direction == StrategyDirection.Long ? exited * price - fee : returned + (returned - exited * price) - fee;
        // An exit of the whole position takes all it invested: the average price may not divide exactly.
        decimal cost = exited == Quantity ? Invested : AveragePrice!.Value * exited;
        RealizedPnl += (direction == StrategyDirection.Long ? exited * price - cost : cost - exited * price) - fee;
        Fees += fee;
        Collateral -= returned;
        Invested -= cost;
        Quantity -= exited;
        if (Quantity < minQuantity || Quantity == 0m)
        {
            _closing = true;
        }

        return cash;
    }
}