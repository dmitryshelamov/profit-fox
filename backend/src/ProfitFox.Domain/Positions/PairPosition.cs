using ProfitFox.Domain.Trades;
using ProfitFox.Domain.Execution;

namespace ProfitFox.Domain.Positions;

/// <summary>
///     The position of a strategy on one pair (spec strategy-position «Одна позиция на пару»): at most one, and the unit
///     is the cycle from its opening to its close. The pair is busy until the exit of its current cycle; a new pair, or a
///     new run, starts with the position closed. Another pair is another object.
/// </summary>
public sealed class PairPosition
{
    /// <summary>
    ///     The exit of the last cycle that was opened: an order before it is skipped. Null while no cycle was opened.
    /// </summary>
    public DateTimeOffset? BusyUntil { get; private set; }

    /// <summary>
    ///     Takes the entry order of <paramref name="order" />. An order before the end of the current cycle is skipped:
    ///     null, and nothing changes. An order at the end of the cycle or later is taken with the position closed: it opens
    ///     a cycle and holds the pair until the exit of its trade, which a stop may bring early. An order that gives no
    ///     plan, or whose trade is left out, opens nothing and does not hold the pair. Orders of a pair come in the order
    ///     of time.
    /// </summary>
    public Cycle? Enter(MinutePath path, StrategyStep order, TradeCosts costs, bool withOutcome = true)
    {
        if (order.Entry is not { } side)
        {
            throw new ArgumentException("The step has no entry order.", nameof(order));
        }

        if (order.Moment < BusyUntil)
        {
            return null;
        }

        Cycle cycle = Cycle.Open(path, order, side, costs, withOutcome);
        if (cycle.Opened)
        {
            BusyUntil = cycle.ExitTime;
        }

        return cycle;
    }
}
