namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     The backtest and the set a backtest on the rest of history was asked from.
/// </summary>
public sealed record BacktestSourceResponse(string Backtest, int Set);
