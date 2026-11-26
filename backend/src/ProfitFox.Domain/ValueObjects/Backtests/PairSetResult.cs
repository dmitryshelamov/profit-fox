using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     What a set gave on a pair: its numbers, and what they are made of — the rows of the orders of a category of ideas and
///     the random entries the pair took for the orders of other pairs, or the cycles and orders of a strategy for a bot. The
///     numbers of a category of ideas have no differences yet: they are made at the end of the backtest from the cells of
///     control of the set.
/// </summary>
public sealed record PairSetResult(int Set, SetNumbers Numbers, IReadOnlyList<EventRow> Rows, PositionResult? Cycles)
{
    public IReadOnlyList<RandomTrade> Randoms { get; init; } = [];
}