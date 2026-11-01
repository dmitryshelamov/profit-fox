using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>The money and the position of a run after an event.</summary>
/// <param name="Direction"></param>
/// <param name="Cash">Money of the bot, including what standing limit entries reserve.</param>
/// <param name="Quantity"></param>
/// <param name="Invested"></param>
/// <param name="Collateral">What a short holds in the cash it paid for its entries.</param>
/// <param name="Dust">What positions closed below the minimum quantity left over: coins of a long, contracts of a short.</param>
/// <param name="DustCollateral">The collateral of the contracts a short left over.</param>
/// <param name="Reserved">What standing limit entries reserve of the cash.</param>
/// <param name="SkippedOrders"></param>
public sealed record RunState(
    StrategyDirection Direction,
    decimal Cash,
    decimal Quantity,
    decimal Invested,
    decimal Collateral,
    decimal Dust,
    decimal DustCollateral,
    decimal Reserved,
    long SkippedOrders)
{
    /// <summary>
    ///     The cash plus the position and the dust at <paramref name="price" />, with no fee of a hypothetical exit. A short is worth
    ///     its collateral plus the fall of the price, its left over contracts too.
    /// </summary>
    public decimal Equity(decimal price)
    {
        decimal position = Direction == StrategyDirection.Long ? Quantity * price : Collateral + (Collateral - Quantity * price);

        return Cash + position + DustValue(price);
    }

    /// <summary>What the dust is worth at <paramref name="price" />: coins of a long at the price, contracts of a short like a short.</summary>
    public decimal DustValue(decimal price)
    {
        return Direction == StrategyDirection.Long ? Dust * price : DustCollateral + (DustCollateral - Dust * price);
    }

    /// <summary>What closing the position at <paramref name="price" /> would gain over the invested amount, before the exit fee.</summary>
    public decimal Unrealized(decimal price)
    {
        return Direction == StrategyDirection.Long ? Quantity * price - Invested : Invested - Quantity * price;
    }
}