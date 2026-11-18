using ProfitFox.Bench.Hypotheses;

namespace ProfitFox.Bench.Runs;

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
