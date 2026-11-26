namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     The numbers of a set of a category of ideas by weeks (spec strategy-backtest «Числа набора по неделям», «Сумма без
///     лучших недель»): the weeks with trades, the mean of the weekly means of the result of a trade after costs as a
///     fraction, the weeks with a mean above zero, the number of the best weeks — those with the largest sums in USDT, a tenth
///     of the weeks and no fewer than three — and the sum in USDT of the other weeks, null with no more weeks than best ones.
/// </summary>
public sealed record WeeklyNumbersResponse(int Weeks, double Mean, int WeeksUp, int BestWeeks, double? WithoutBest);