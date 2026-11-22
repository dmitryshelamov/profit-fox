namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The numbers of a set on a pair or on every pair (spec strategy-backtest «Результат набора», «Результат по парам»;
///     change add-strategy-backtest, design.md, decision 5). The numbers of a set are the sum of those of its pairs, so
///     they are kept as sums, and the means and shares are made from them. A trade is a closed cycle: an open one is
///     counted apart, and its estimate is not in the sum. <see cref="Differences" /> are the differences of the orders that
///     count, in the order the pairs and their orders came: the weekly means are made from them.
/// </summary>
public sealed record SetNumbers
{
    public static readonly SetNumbers Zero = new SetNumbers();

    /// <summary>
    ///     The orders in history: those that opened a cycle, gave no trade or were skipped.
    /// </summary>
    public int Orders { get; init; }

    public int Trades { get; init; }

    public int PairsWithTrades { get; init; }

    /// <summary>
    ///     The sum of the results of the trades after costs, as fractions of what was put in.
    /// </summary>
    public double NetSum { get; init; }

    /// <summary>
    ///     The sum of the results of the trades in USDT.
    /// </summary>
    public double Amount { get; init; }

    public int Wins { get; init; }

    public Exclusions Excluded { get; init; } = Exclusions.None;

    public int Skipped { get; init; }

    public int OpenCycles { get; init; }

    /// <summary>
    ///     The sum of the estimates of the open cycles in USDT, at the last price.
    /// </summary>
    public double OpenAmount { get; init; }

    public IReadOnlyList<WeekDifference> Differences { get; init; } = [];

    /// <summary>
    ///     The mean result of a trade after costs; none without trades.
    /// </summary>
    public double? Mean => Trades > 0 ? NetSum / Trades : null;

    /// <summary>
    ///     The share of the trades with a result above zero; none without trades.
    /// </summary>
    public double? WinShare => Trades > 0 ? (double)Wins / Trades : null;

    /// <summary>
    ///     The mean of «the order minus the mean of its random entries» of each week that has orders that count, in the
    ///     order of the weeks: the means of the weeks of every pair, as the bench always made them.
    /// </summary>
    public IReadOnlyList<(long Week, double Mean, int Orders)> WeekMeans()
    {
        return [.. Differences.GroupBy(item => item.Week).OrderBy(week => week.Key).Select(week => (week.Key, week.Average(item => item.Difference), week.Count()))];
    }

    /// <summary>
    ///     The difference from random entries; none when no order counts. A week starts on Monday 00:00 UTC; the t of the mean
    ///     of the weekly means needs at least two weeks that differ.
    /// </summary>
    public RandomDifference? Difference()
    {
        double[] means = [.. WeekMeans().Select(week => week.Mean)];
        if (means.Length == 0)
        {
            return null;
        }

        double mean = means.Average();
        if (means.Length < 2)
        {
            return new RandomDifference(mean, means.Length, T: null);
        }

        double sd = Math.Sqrt(means.Sum(value => (value - mean) * (value - mean)) / (means.Length - 1));
        return new RandomDifference(mean, means.Length, sd > 0 ? mean / (sd / Math.Sqrt(means.Length)) : null);
    }

    public static SetNumbers operator +(SetNumbers a, SetNumbers b)
    {
        return new SetNumbers
        {
            Orders = a.Orders + b.Orders,
            Trades = a.Trades + b.Trades,
            PairsWithTrades = a.PairsWithTrades + b.PairsWithTrades,
            NetSum = a.NetSum + b.NetSum,
            Amount = a.Amount + b.Amount,
            Wins = a.Wins + b.Wins,
            Excluded = a.Excluded + b.Excluded,
            Skipped = a.Skipped + b.Skipped,
            OpenCycles = a.OpenCycles + b.OpenCycles,
            OpenAmount = a.OpenAmount + b.OpenAmount,
            Differences = b.Differences.Count == 0 ? a.Differences : a.Differences.Count == 0 ? b.Differences : [.. a.Differences, .. b.Differences]
        };
    }
}
