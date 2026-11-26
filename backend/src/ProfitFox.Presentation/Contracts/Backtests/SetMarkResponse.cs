namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     The mark of a set of a category of ideas of engine version 5 and later (spec strategy-backtest «Отметка набора»):
///     <c>candidate</c>, <c>passed</c>, <c>failed</c>, <c>unproven</c>, or null when a set of a backtest without a source did
///     not meet all five conditions; and which of the five conditions the set met. «passed», «failed» and «unproven» are
///     given only to a set of a backtest on the rest of history. The mark limits no backtest.
/// </summary>
public sealed record SetMarkResponse(string? Value, MarkConditionsResponse Conditions);

/// <summary>
///     Which of the five conditions of the mark a set met (spec strategy-backtest «Условия отметки набора»): trades in at
///     least 20 weeks; the mean of the weekly means above zero; at least half the weeks up; the sum without the best weeks
///     above zero; the difference from random entries above zero with a t of at least 2.
/// </summary>
public sealed record MarkConditionsResponse(bool Weeks, bool WeeklyMean, bool WeeksUp, bool WithoutBest, bool Difference);