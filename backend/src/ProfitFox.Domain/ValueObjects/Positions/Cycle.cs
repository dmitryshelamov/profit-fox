using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.ValueObjects.Positions;

/// <summary>
///     A cycle of the position of a strategy by plan on a pair (spec strategy-position): the cycle of an entry from its
///     opening to its close, or why there is none. <see cref="Status" /> is <see cref="TradeStatus.Ok" /> for an open cycle,
///     <see cref="TradeStatus.Plan" /> for an entry that gave no plan, and the reason of the calculation of a cycle for a
///     cycle left out. <see cref="Result" /> is what <see cref="CycleRun" /> counted, or null when only the end of the cycle
///     was asked for.
/// </summary>
public sealed record Cycle(TradeStatus Status, TradePlan? Plan, DateTimeOffset? ExitTime, CycleOutcome? Result)
{
    /// <summary>
    ///     The cycle as the trade of its plan, built from <see cref="Result" />: what the bench, its journal and its report
    ///     read. Null when only the end of the cycle was asked for.
    /// </summary>
    public TradeOutcome? Outcome { get; } = Result?.ToTrade();

    /// <summary>
    ///     The cycle is open from its entry to <see cref="ExitTime" />; any other status opens no position.
    /// </summary>
    public bool Opened => Status == TradeStatus.Ok;

    /// <summary>
    ///     Opens a cycle with the position closed, whatever other cycles the pair has: at the moment of
    ///     <paramref name="step" />, on the given side, with the size, the time, the stop and the target the nodes give at
    ///     this candle. This is how a cycle is opened outside the graph, and how <see cref="PairPosition" /> opens the cycle
    ///     of an order. The cycle is counted by <see cref="CycleRun" /> (change add-position-node, design.md, decision 5);
    ///     without <paramref name="withOutcome" /> it is walked only to its end: its result is not computed.
    /// </summary>
    public static Cycle Open(MinutePath path, StrategyStep step, EntrySide side, TradeCosts costs, bool withOutcome = true)
    {
        if (EntryPlan.Of(step, side) is not { } plan)
        {
            return new Cycle(TradeStatus.Plan, Plan: null, ExitTime: null, withOutcome ? CycleOutcome.Excluded(TradeStatus.Plan, side) : null);
        }

        if (withOutcome)
        {
            // A step that gives a plan has a size above zero.
            CycleOutcome result = CycleRun.Simulate(path, step.Moment, plan, costs, (double)step.Size!.Value, step.Node);
            return new Cycle(result.Status, plan, result.Status == TradeStatus.Ok ? result.Fills[^1].Moment : null, result);
        }

        TradeExit exit = CycleRun.Exit(path, step.Moment, plan, costs);
        return new Cycle(exit.Status, plan, exit.Time, Result: null);
    }
}
