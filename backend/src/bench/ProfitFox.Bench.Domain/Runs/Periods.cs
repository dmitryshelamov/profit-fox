namespace ProfitFox.Bench.Domain.Runs;

/// <summary>
///     The fixed dates of the periods, UTC (spec hypothesis-bench): exploration up to 2026-05-15, holdout 2026-05-16 to
///     2026-11-15, new data from 2026-11-16 to the start of the current day.
/// </summary>
public static class Periods
{
    /// <summary>
    ///     Earlier than any history the service keeps: the start of exploration and of every signal read.
    /// </summary>
    public static readonly DateTimeOffset DataStart = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset ExplorationEnd = new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset HoldoutEnd = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     [start, end) of the period; <paramref name="today" /> is the start of the current UTC day.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Span(Period period, DateTimeOffset today)
    {
        return period switch
        {
            Period.Exploration => (DataStart, ExplorationEnd),
            Period.Holdout => (ExplorationEnd, HoldoutEnd),
            Period.NewData => (HoldoutEnd, today),
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, message: null)
        };
    }

    public static string Name(Period period)
    {
        return period switch
        {
            Period.Exploration => "exploration",
            Period.Holdout => "holdout",
            Period.NewData => "new-data",
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, message: null)
        };
    }

    public static bool TryParse(string name, out Period period)
    {
        foreach (Period candidate in Enum.GetValues<Period>())
        {
            if (Name(candidate) == name)
            {
                period = candidate;
                return true;
            }
        }

        period = default;
        return false;
    }
}
