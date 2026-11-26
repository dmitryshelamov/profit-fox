using ProfitFox.Domain.Services.Runs;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     What a backtest gave a set: its numbers over every pair, the numbers of each pair in the order of the pairs, and for a
///     category of ideas the cells of control its differences were made from.
/// </summary>
public sealed record SetTotals(SetNumbers Numbers, IReadOnlyList<(string Pair, SetNumbers Numbers)> Pairs, ControlCells? Cells);