namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     The difference from random entries (spec strategy-backtest «Разница со случайным входом»): the mean of the weekly
///     means as a fraction, the number of weeks and its t, null with fewer than two weeks. From engine version 5 a random
///     entry is another pair of the control group of the week at the moment of the order; the differences of older backtests
///     were counted on random moments of the same week on the same pair and are not comparable with them.
/// </summary>
public sealed record RandomDifferenceResponse(double Mean, int Weeks, double? T);