namespace ProfitFox.Application.Ports.MarketData;

/// <summary>
///     What the cache of history of the process did since it started (change add-backtest-history-cache, design.md, decision
///     9); a process without the cache counts nothing.
/// </summary>
public interface IHistoryCacheCounters
{
    HistoryCacheCounts Counts { get; }
}

/// <summary>
///     Running totals of the cache of history.
/// </summary>
/// <param name="FromCache">Months taken from the cache.</param>
/// <param name="Saved">Months saved into the cache.</param>
/// <param name="Requests">Requests sent to the market-data service.</param>
public sealed record HistoryCacheCounts(long FromCache, long Saved, long Requests)
{
    public static readonly HistoryCacheCounts Zero = new HistoryCacheCounts(0, 0, 0);

    public static HistoryCacheCounts operator -(HistoryCacheCounts after, HistoryCacheCounts before)
    {
        return new HistoryCacheCounts(after.FromCache - before.FromCache, after.Saved - before.Saved, after.Requests - before.Requests);
    }
}
