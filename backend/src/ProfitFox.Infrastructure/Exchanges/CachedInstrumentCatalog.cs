using System.Collections.Concurrent;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Exchanges;

/// <summary>
///     Keeps the list of a category for five minutes: a search runs on every key press and the list changes rarely. A failure is
///     not kept.
/// </summary>
internal sealed class CachedInstrumentCatalog(IInstrumentCatalog inner, TimeProvider time) : IInstrumentCatalog
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<MarketCategory, (DateTimeOffset Expires, IReadOnlyList<ListedInstrument> Listed)> _cache = [];

    public async Task<IReadOnlyList<ListedInstrument>> ListAsync(MarketCategory category, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();

        if (_cache.TryGetValue(category, out (DateTimeOffset Expires, IReadOnlyList<ListedInstrument> Listed) cached) && now < cached.Expires)
        {
            return cached.Listed;
        }

        IReadOnlyList<ListedInstrument> listed = await inner.ListAsync(category, cancellationToken);
        _cache[category] = (now + Lifetime, listed);

        return listed;
    }
}