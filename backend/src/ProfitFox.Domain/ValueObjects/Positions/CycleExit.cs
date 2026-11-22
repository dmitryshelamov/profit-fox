namespace ProfitFox.Domain.ValueObjects.Positions;

/// <summary>
///     How a cycle closed (spec strategy-position «Результат цикла»): by the stop, the target or the time, as a trade of a
///     plan ends, or by a fill of a node <c>limit-order</c>, <c>market-order</c> or <c>close-position</c>; <see cref="None" />
///     for a cycle left out or still open.
/// </summary>
public enum CycleExit
{
    None,
    Stop,
    Target,
    Time,
    LimitOrder,
    MarketOrder,
    ClosePosition
}
