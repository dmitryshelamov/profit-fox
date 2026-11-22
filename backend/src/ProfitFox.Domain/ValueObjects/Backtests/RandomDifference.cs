namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The difference from random entries (spec strategy-backtest «Разница со случайным входом»): the mean of the weekly
///     means, the number of weeks and the t of that mean, which needs two weeks and a spread.
/// </summary>
public sealed record RandomDifference(double Mean, int Weeks, double? T);
