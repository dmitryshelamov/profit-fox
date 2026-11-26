namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The numbers of a set by weeks (spec strategy-backtest «Числа набора по неделям», «Сумма без лучших недель»; change
///     fix-backtest-random-control, design.md, decision 9): the weeks with trades, the mean of the weekly means of the result
///     of a trade after costs, the weeks whose mean is above zero, the number of the best weeks and the sum in USDT of the
///     other weeks. The best weeks are those with the largest sums in USDT, a tenth of the weeks rounded up and no fewer than
///     three; of two weeks with the same sum the earlier one is the better. With no more weeks than best ones there is no sum
///     without them.
/// </summary>
public sealed record WeeklyNumbers(int Weeks, double Mean, int WeeksUp, int BestWeeks, double? WithoutBest)
{
    /// <summary>
    ///     The best weeks are this part of the weeks with trades, rounded up.
    /// </summary>
    public const int BestShare = 10;

    /// <summary>
    ///     The fewest best weeks.
    /// </summary>
    public const int FewestBest = 3;

    /// <summary>
    ///     The numbers of the weeks of a set; none without a week with trades.
    /// </summary>
    public static WeeklyNumbers? Of(IReadOnlyList<WeekTrades> weeks)
    {
        WeekTrades[] traded = [.. weeks.Where(week => week.Trades > 0).OrderBy(week => week.Week)];
        if (traded.Length == 0)
        {
            return null;
        }

        int best = Math.Max(FewestBest, (traded.Length + BestShare - 1) / BestShare);
        HashSet<long> top = [.. traded.OrderByDescending(week => week.Amount).ThenBy(week => week.Week).Take(best).Select(week => week.Week)];
        double? without = traded.Length > best ? traded.Where(week => !top.Contains(week.Week)).Sum(week => week.Amount) : null;
        return new WeeklyNumbers(traded.Length, traded.Average(week => week.Mean), traded.Count(week => week.Mean > 0), best, without);
    }
}