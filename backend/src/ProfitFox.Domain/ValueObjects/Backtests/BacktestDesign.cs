using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     What a set of a backtest is computed on (change add-strategy-backtest, design.md, decision 4): history from
///     <see cref="Start" /> to <see cref="End" />, the end not included, the timeframe of its candles and the costs. The time
///     of an order comes from its step, not from a plan: an order is in the history when its moment is not before the start
///     and its moment plus its time not after the end; a random entry by the same rule with the time of its step on its own
///     pair. A block is a week and a control group has up to 20 pairs (change fix-backtest-random-control, design.md,
///     decision 3) unless a test gives others.
/// </summary>
public sealed record BacktestDesign(DateTimeOffset Start, DateTimeOffset End, TimeSpan Timeframe, TradeCosts Costs)
{
    public TimeBlock Block { get; init; } = TimeBlock.Week;

    /// <summary>
    ///     The most pairs of a control group.
    /// </summary>
    public int Randoms { get; init; } = Backtest.Randoms;

    public TimeSpan BlockLength => Block == TimeBlock.Day ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7);

    /// <summary>
    ///     Whether a trade from <paramref name="moment" /> with the time <paramref name="after" /> is in the history; a
    ///     missing time counts as none, and the trade is then refused by its plan.
    /// </summary>
    public bool Fits(DateTimeOffset moment, TimeSpan? after)
    {
        return moment >= Start && moment + (after ?? TimeSpan.Zero) <= End;
    }

    /// <summary>
    ///     The minutes the trades of a block can touch: from the block (or the start) to its end plus
    ///     <paramref name="longest" />, never past the end.
    /// </summary>
    public (DateTimeOffset From, DateTimeOffset To) Span(long block, TimeSpan longest)
    {
        DateTimeOffset start = Blocks.Start(block, Block);
        DateTimeOffset from = start > Start ? start : Start;
        DateTimeOffset end = start + BlockLength + longest;
        return (from, end < End ? end : End);
    }
}