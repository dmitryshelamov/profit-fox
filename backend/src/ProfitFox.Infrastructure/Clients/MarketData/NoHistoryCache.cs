using ProfitFox.Application.Ports.MarketData;

namespace ProfitFox.Infrastructure.Clients.MarketData;

/// <summary>
///     The counters of a process without the cache of history: nothing is taken from it, saved or asked through it.
/// </summary>
internal sealed class NoHistoryCache : IHistoryCacheCounters
{
    public HistoryCacheCounts Counts => HistoryCacheCounts.Zero;
}
