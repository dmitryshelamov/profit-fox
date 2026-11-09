using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Domain.Services.IdeaTrades;

/// <summary>Splits a liquidation or an auto-deleveraging of the exchange between the trades of the side it closes.</summary>
public static class ForcedFillShares
{
    /// <summary>
    ///     An execution of all the holders hold or more gives each its whole quantity. A smaller one gives each its part by the quantity it
    ///     holds, rounded down to <paramref name="step" />, and the last holder takes what the rounding leaves, no more than it holds. Every share
    ///     is at the price of the execution and takes its part of the fee by its quantity.
    /// </summary>
    /// <param name="quantity"></param>
    /// <param name="price"></param>
    /// <param name="fee"></param>
    /// <param name="step"></param>
    /// <param name="holders">The trades that hold the side the execution closes, in the order of their ids.</param>
    public static IReadOnlyList<ForcedFillShare> Split(decimal quantity, decimal price, decimal fee, decimal step, IReadOnlyList<ForcedFillHolder> holders)
    {
        decimal held = holders.Sum(holder => holder.Held);
        decimal left = quantity;
        var shares = new List<ForcedFillShare>();

        for (var i = 0; i < holders.Count; i++)
        {
            ForcedFillHolder holder = holders[i];
            decimal part = quantity >= held ? holder.Held
                : i == holders.Count - 1 ? Math.Min(left, holder.Held)
                : Math.Floor(quantity * holder.Held / held / step) * step;
            left -= part;
            shares.Add(new ForcedFillShare(holder.TradeId, new LegTotals(part, part * price, fee * part / quantity)));
        }

        return shares;
    }
}