namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The difference of an order that counts (spec strategy-backtest «Разница со случайным входом»): its result after
///     costs minus the mean of its random entries, with the week of its moment.
/// </summary>
public readonly record struct WeekDifference(long Week, double Difference);
