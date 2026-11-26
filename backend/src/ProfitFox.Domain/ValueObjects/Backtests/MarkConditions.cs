namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     Which of the five conditions of the mark a set met (spec strategy-backtest «Условия отметки набора»): trades in at
///     least 20 weeks; the mean of the weekly means above zero; at least half the weeks up; the sum without the best weeks
///     above zero; the difference from random entries above zero with a t of at least 2. A condition whose number the set does
///     not have is not met.
/// </summary>
public sealed record MarkConditions(bool Weeks, bool WeeklyMean, bool WeeksUp, bool WithoutBest, bool Difference)
{
    /// <summary>
    ///     The fewest weeks with trades.
    /// </summary>
    public const int FewestWeeks = 20;

    /// <summary>
    ///     The smallest t of the difference.
    /// </summary>
    public const double FewestT = 2;

    public bool All => Weeks && WeeklyMean && WeeksUp && WithoutBest && Difference;

    public static MarkConditions Of(SetSummary numbers)
    {
        WeeklyNumbers? weekly = numbers.Weekly;
        RandomDifference? difference = numbers.Difference;
        return new MarkConditions(
            weekly is { Weeks: >= FewestWeeks },
            weekly is { Mean: > 0 },
            weekly is { } up && up.WeeksUp * 2 >= up.Weeks,
            weekly is { WithoutBest: > 0 },
            difference is { Mean: > 0, T: >= FewestT });
    }
}