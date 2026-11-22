namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     A set of a backtest (spec strategy-backtest «Наборы значений»): its number from 1 in the order of the grid, its
///     timeframe and one value of each parameter the request gave values to, in the order of the parameters of the graph.
///     A parameter without values in the request is not here: it keeps the value of the graph.
/// </summary>
public sealed record BacktestSet(int Number, TimeSpan Timeframe, IReadOnlyList<KeyValuePair<string, string>> Values);
