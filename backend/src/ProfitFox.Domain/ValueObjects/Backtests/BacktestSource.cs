namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The backtest and its set a backtest on the rest of history was asked from.
/// </summary>
public sealed record BacktestSource(string Backtest, int Set);
