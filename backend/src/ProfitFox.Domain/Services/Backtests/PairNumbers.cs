using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>
///     The numbers of a set on one pair (change add-strategy-backtest, design.md, decision 5), from what its calculation
///     gave: the rows of the orders of a category of ideas, or the cycles and orders of a strategy for a bot.
/// </summary>
public static class PairNumbers
{
    /// <summary>
    ///     A category of ideas: a trade is the cycle of an order that opened one; an order that opened none is counted by its
    ///     reason. The differences are not here: they need the cells of control of every pair (change
    ///     fix-backtest-random-control, design.md, decision 5), and the backtest adds them at its end.
    /// </summary>
    public static SetNumbers Of(IReadOnlyList<EventRow> rows, int skipped, int pastEnd)
    {
        EventRow[] trades = [.. rows.Where(row => row.Status == TradeStatus.Ok)];
        return new SetNumbers
        {
            Orders = rows.Count + skipped + pastEnd,
            Trades = trades.Length,
            PairsWithTrades = trades.Length > 0 ? 1 : 0,
            NetSum = trades.Sum(row => row.Outcome!.Net!.Value),
            Amount = trades.Sum(row => row.NetAmount!.Value),
            Wins = trades.Count(row => row.Outcome!.Net > 0),
            Excluded = new Exclusions(
                rows.Count(row => row.Status == TradeStatus.Plan),
                rows.Count(row => row.Status == TradeStatus.Coverage),
                rows.Count(row => row.Status == TradeStatus.Funding),
                rows.Count(row => row.Status == TradeStatus.NoTrade),
                pastEnd),
            Skipped = skipped
        };
    }

    /// <summary>
    ///     The pairs of a set as its result gives them (spec strategy-backtest «Результат по парам»): those with at least one
    ///     order, by the sum of their results from the highest, then by symbol.
    /// </summary>
    public static IReadOnlyList<(string Pair, SetNumbers Numbers)> Ordered(IEnumerable<(string Pair, SetNumbers Numbers)> pairs)
    {
        return [.. pairs.Where(pair => pair.Numbers.Orders > 0).OrderByDescending(pair => pair.Numbers.Amount).ThenBy(pair => pair.Pair, StringComparer.Ordinal)];
    }

    /// <summary>
    ///     A strategy for a bot: a trade is a closed cycle, its result the fraction of the largest money put in; a cycle open
    ///     at the end of history is counted apart with its estimate; a cycle left out, such as one a hole in the minutes broke,
    ///     is without a trade by its reason (task 12.3); orders by their outcome. It has no random entries.
    /// </summary>
    public static SetNumbers Of(PositionResult result)
    {
        CycleOutcome[] closed = [.. result.Cycles.Where(cycle => cycle is { Open: false, Status: TradeStatus.Ok })];
        CycleOutcome[] open = [.. result.Cycles.Where(cycle => cycle.Open)];
        CycleOutcome[] left = [.. result.Cycles.Where(cycle => cycle is { Open: false } && cycle.Status != TradeStatus.Ok)];
        OrderOutcome[] excluded = [.. result.Orders.Where(order => order.Result == OrderResult.Excluded)];
        return new SetNumbers
        {
            Orders = result.Orders.Count,
            Trades = closed.Length,
            PairsWithTrades = closed.Length > 0 ? 1 : 0,
            NetSum = closed.Sum(cycle => cycle.Net!.Value),
            Amount = closed.Sum(cycle => cycle.NetAmount!.Value),
            Wins = closed.Count(cycle => cycle.Net > 0),
            Excluded = new Exclusions(
                excluded.Count(order => order.Exclusion == OrderExclusion.Plan) + left.Count(cycle => cycle.Status == TradeStatus.Plan),
                excluded.Count(order => order.Exclusion == OrderExclusion.Coverage) + left.Count(cycle => cycle.Status == TradeStatus.Coverage),
                excluded.Count(order => order.Exclusion == OrderExclusion.Funding) + left.Count(cycle => cycle.Status == TradeStatus.Funding),
                excluded.Count(order => order.Exclusion == OrderExclusion.NoTrade) + left.Count(cycle => cycle.Status == TradeStatus.NoTrade),
                excluded.Count(order => order.Exclusion == OrderExclusion.Period)),
            Skipped = result.Orders.Count(order => order.Result == OrderResult.Skipped),
            OpenCycles = open.Length,
            OpenAmount = open.Sum(cycle => cycle.NetAmount ?? 0)
        };
    }
}