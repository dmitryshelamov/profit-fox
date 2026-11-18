namespace ProfitFox.Bench.MarketData.History;

/// <summary>
///     Covered parts of time: what the history knows. Inside them everything the exchange had is told, outside nothing is.
/// </summary>
public static class Coverage
{
    /// <summary>
    ///     The ranges ordered, with touching and overlapping ones joined.
    /// </summary>
    public static IReadOnlyList<TimeRange> Merge(IEnumerable<TimeRange> ranges)
    {
        List<TimeRange> merged = [];
        foreach (TimeRange range in ranges.Where(range => range.Until > range.From).OrderBy(range => range.From))
        {
            if (merged.Count > 0 && range.From <= merged[^1].Until)
            {
                merged[^1] = merged[^1] with { Until = range.Until > merged[^1].Until ? range.Until : merged[^1].Until };
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }

    /// <summary>
    ///     The ranges cut to [<paramref name="from" />, <paramref name="until" />).
    /// </summary>
    public static IReadOnlyList<TimeRange> Trim(IEnumerable<TimeRange> ranges, DateTimeOffset from, DateTimeOffset until)
    {
        return Merge(ranges
            .Select(range => new TimeRange(range.From > from ? range.From : from, range.Until < until ? range.Until : until))
            .Where(range => range.Until > range.From));
    }

    public static bool Covers(IReadOnlyList<TimeRange> ranges, DateTimeOffset from, DateTimeOffset until)
    {
        return Merge(ranges).Any(range => range.Contains(from, until));
    }
}
