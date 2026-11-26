namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The difference from random entries (spec strategy-backtest «Разница со случайным входом»): the mean of the weekly
///     means of «the result of an order minus the mean of its valid random entries», the number of weeks and the t of that
///     mean, which needs two weeks and a spread. From engine version 5 (change fix-backtest-random-control) the random
///     entries of an order are the pairs of the control group of its week at its moment, on its side.
/// </summary>
public sealed record RandomDifference(double Mean, int Weeks, double? T);