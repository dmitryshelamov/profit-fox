using ProfitFox.Domain.Services.Hypotheses;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.Runs.Enums;

namespace ProfitFox.Domain.ValueObjects.Runs;

/// <summary>
///     A hypothesis on one period: what a run of it compares and where. An event belongs to the period when its trade with
///     the whole horizon fits into it.
/// </summary>
public sealed record RunDesign(Hypothesis Hypothesis, Period Period, DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd)
{
    /// <summary>
    ///     The latest entry whose horizon ends inside the period.
    /// </summary>
    public DateTimeOffset LastEntry => PeriodEnd - Hypothesis.Plan.Horizon;

    public TimeSpan BlockLength => HypothesisReader.Length(Hypothesis.Block);

    /// <summary>
    ///     The design of a backtest the run of the hypothesis is computed with (change add-strategy-backtest, task 4.1): the
    ///     period as its history, the timeframe, block, random entries, measure and costs of the hypothesis, and its hash as
    ///     the seed. The time of each order is that of its step, which is the horizon of the plan.
    /// </summary>
    public BacktestDesign ToBacktest()
    {
        return new BacktestDesign(PeriodStart, PeriodEnd, Hypothesis.Timeframe, Hypothesis.Hash, Hypothesis.Costs)
        {
            Block = Hypothesis.Block,
            Randoms = Hypothesis.RandomsPerEvent,
            Measure = Hypothesis.Measure
        };
    }

    public bool InPeriod(DateTimeOffset moment)
    {
        return moment >= PeriodStart && moment <= LastEntry;
    }

    /// <summary>
    ///     The span of minutes the trades of a block can touch: from the block (or the period) start to its end plus the
    ///     horizon, never past the end of the period.
    /// </summary>
    public (DateTimeOffset From, DateTimeOffset To) Span(long block)
    {
        DateTimeOffset start = Blocks.Start(block, Hypothesis.Block);
        DateTimeOffset from = start > PeriodStart ? start : PeriodStart;
        DateTimeOffset end = start + BlockLength + Hypothesis.Plan.Horizon;
        return (from, end < PeriodEnd ? end : PeriodEnd);
    }
}
