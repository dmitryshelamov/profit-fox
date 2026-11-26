using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     An order of a pair as the other pairs of a backtest see it (change fix-backtest-random-control, design.md, decision
///     4): its moment and its side, what a random entry of the order takes.
/// </summary>
public readonly record struct PairOrder(DateTimeOffset Moment, EntrySide Side);