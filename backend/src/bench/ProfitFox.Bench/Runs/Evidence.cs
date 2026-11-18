namespace ProfitFox.Bench.Runs;

/// <summary>
///     Block means over the events that count (spec hypothesis-bench): the unit of evidence is a block of time, the mean of
///     its events of every pair.
/// </summary>
public static class Evidence
{
    /// <summary>
    ///     Blind: the mean pseudo difference of each block, from random entries only, in time order.
    /// </summary>
    public static IReadOnlyList<(long Block, double Mean, int Events)> PseudoBlockMeans(IEnumerable<EventRow> rows)
    {
        return [.. rows.Where(row => row.Usable)
            .GroupBy(row => row.Block)
            .OrderBy(group => group.Key)
            .Select(group => (group.Key, group.Average(row => row.PseudoDifference!.Value), group.Count()))];
    }

    /// <summary>
    ///     The mean of «event minus the mean of its random entries» of each block, in time order. Reads the outcomes of
    ///     events: only after the power check.
    /// </summary>
    public static IReadOnlyList<(long Block, double Mean, int Events)> BlockMeans(IEnumerable<EventRow> rows)
    {
        return [.. rows.Where(row => row.Usable)
            .GroupBy(row => row.Block)
            .OrderBy(group => group.Key)
            .Select(group => (group.Key, group.Average(row => row.Event!.Y - row.RandomMean!.Y), group.Count()))];
    }
}
