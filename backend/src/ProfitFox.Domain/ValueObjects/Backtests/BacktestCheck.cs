namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The settings of a backtest when the request has no error, or every error of the request.
/// </summary>
public sealed record BacktestCheck(BacktestSettings? Settings, IReadOnlyList<BacktestError> Errors);
