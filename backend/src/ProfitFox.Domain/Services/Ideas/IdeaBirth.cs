using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     How an idea is born (change add-ideas-and-market, design.md, decision 5; spec strategy-ideas «Идея»): an entry order of
///     an enabled strategy at a closed candle with an allowed plan (<see cref="EntryPlan.Of" />) becomes an idea; an order
///     without a plan does not, as an order of a backtest without one gives no trade.
/// </summary>
public static class IdeaBirth
{
    /// <summary>
    ///     The idea of <paramref name="step" />, the step of the strategy at the close of <paramref name="candle" />, or null
    ///     when the step has no order or no plan.
    /// </summary>
    public static Idea? Of(
        StrategyStep step,
        ClosedCandle candle,
        Guid strategyId,
        string strategyName,
        int strategyVersion,
        TimeSpan timeframe,
        string symbol,
        IReadOnlyList<string> labels)
    {
        if (step.Entry is not { } side || EntryPlan.Of(step, side) is not { } plan)
        {
            return null;
        }

        return new Idea
        {
            Id = Guid.NewGuid(),
            StrategyId = strategyId,
            StrategyName = strategyName,
            StrategyVersion = strategyVersion,
            Timeframe = timeframe,
            Symbol = symbol,
            Side = plan.Side == TradeSide.Long ? ValueObjects.Graph.Enums.EntrySide.Long : ValueObjects.Graph.Enums.EntrySide.Short,
            Moment = step.Moment,
            Size = step.Size!.Value,
            After = plan.Horizon,
            Stop = plan.Stop,
            Target = plan.Target,
            CandleClose = candle.Close,
            Labels = labels
        };
    }

    /// <summary>
    ///     The plan of an idea, as the cycle walks it.
    /// </summary>
    public static TradePlan Plan(Idea idea)
    {
        return new TradePlan(idea.Side == ValueObjects.Graph.Enums.EntrySide.Long ? TradeSide.Long : TradeSide.Short, idea.After, idea.Stop, idea.Target);
    }
}
