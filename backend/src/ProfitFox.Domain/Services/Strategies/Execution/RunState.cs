using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>The money and the position of a run after an event.</summary>
/// <param name="Direction"></param>
/// <param name="Cash">Money of the bot, including what standing limit entries reserve.</param>
/// <param name="Quantity"></param>
/// <param name="Invested"></param>
/// <param name="Collateral">What a short holds in the cash it paid for its entries.</param>
/// <param name="Reserved">What standing limit entries reserve of the cash.</param>
/// <param name="SkippedOrders"></param>
/// <param name="Position">The number of the current position, from 1.</param>
/// <param name="Status"></param>
/// <param name="RealizedPnl">Of the current position.</param>
/// <param name="Fees">Of the current position, in the quote asset.</param>
/// <param name="Closed">The position the last event closed; <c>null</c> when it closed none.</param>
public sealed record RunState(
    StrategyDirection Direction,
    decimal Cash,
    decimal Quantity,
    decimal Invested,
    decimal Collateral,
    decimal Reserved,
    long SkippedOrders,
    int Position,
    PositionStatus Status,
    decimal RealizedPnl,
    decimal Fees,
    ClosedPosition? Closed)
{
    /// <summary>
    ///     The cash plus the position at <paramref name="price" />, with its remainder and no fee of a hypothetical exit. A short is
    ///     worth its collateral plus the fall of the price.
    /// </summary>
    public decimal Equity(decimal price)
    {
        decimal position = Direction == StrategyDirection.Long ? Quantity * price : Collateral + (Collateral - Quantity * price);

        return Cash + position;
    }

    /// <summary>What closing the position at <paramref name="price" /> would gain over the invested amount, before the exit fee.</summary>
    public decimal Unrealized(decimal price)
    {
        return Direction == StrategyDirection.Long ? Quantity * price - Invested : Invested - Quantity * price;
    }
}