using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.ValueObjects.Positions;

/// <summary>
///     A cycle of the position of a strategy on a pair (spec strategy-position): the trade of an entry from its opening to
///     its close, or why there is none. <see cref="Status" /> is <see cref="TradeStatus.Ok" /> for an open cycle,
///     <see cref="TradeStatus.Plan" /> for an entry that gave no plan, and the reason of the trade simulator for a trade
///     left out. <see cref="Outcome" /> is the outcome of the trade, or null when only the end of the cycle was asked for.
/// </summary>
public sealed record Cycle(TradeStatus Status, TradePlan? Plan, DateTimeOffset? ExitTime, TradeOutcome? Outcome)
{
    /// <summary>
    ///     The cycle is open from its entry to <see cref="ExitTime" />; any other status opens no position.
    /// </summary>
    public bool Opened => Status == TradeStatus.Ok;

    /// <summary>
    ///     Opens a cycle with the position closed, whatever other cycles the pair has: at the moment of
    ///     <paramref name="step" />, on the given side, with the time, the stop and the target the exit nodes give at this
    ///     candle. This is how a cycle is opened outside the graph, and how <see cref="PairPosition" /> opens the cycle of
    ///     an order. Without <paramref name="withOutcome" /> the trade is walked only to its end: its result is not computed.
    /// </summary>
    public static Cycle Open(MinutePath path, StrategyStep step, EntrySide side, TradeCosts costs, bool withOutcome = true)
    {
        if (EntryPlan.Of(step, side) is not { } plan)
        {
            return new Cycle(TradeStatus.Plan, Plan: null, ExitTime: null, withOutcome ? TradeOutcome.Excluded(TradeStatus.Plan) : null);
        }

        if (withOutcome)
        {
            TradeOutcome outcome = TradeSimulator.Simulate(path, step.Moment, plan, costs);
            return new Cycle(outcome.Status, plan, outcome.ExitTime, outcome);
        }

        TradeExit exit = TradeSimulator.Exit(path, step.Moment, plan, costs);
        return new Cycle(exit.Status, plan, exit.Time, Outcome: null);
    }
}
