using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Exchanges;

/// <summary>
///     Keeps the list of a category for five minutes: a search runs on every key press and the list changes rarely. Callers that
///     arrive while the list is being fetched share that one fetch, which a caller leaving does not cancel. A failure is not kept.
///     The catalog behind it is resolved in a scope of its own for every fetch: a client kept for the life of the app would keep
///     one pooled HTTP handler and never see a changed address of the exchange.
/// </summary>
internal sealed class CachedInstrumentCatalog(IServiceScopeFactory scopes, TimeProvider time) : IInstrumentCatalog
{
    /// <summary>The key the catalog behind this one is registered under.</summary>
    public const string InnerKey = "exchange";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly Lock _gate = new();
    private readonly Dictionary<MarketCategory, (DateTimeOffset Expires, Task<IReadOnlyList<ListedInstrument>> Listed)> _cache = [];

    public Task<IReadOnlyList<ListedInstrument>> ListAsync(MarketCategory category, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        Task<IReadOnlyList<ListedInstrument>> listed;

        lock (_gate)
        {
            if (!_cache.TryGetValue(category, out (DateTimeOffset Expires, Task<IReadOnlyList<ListedInstrument>> Listed) cached) || now >= cached.Expires)
            {
                cached = (now + Lifetime, Fetch(category));
                _cache[category] = cached;
            }

            listed = cached.Listed;
        }

        return listed.WaitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ListedInstrument>> Fetch(MarketCategory category)
    {
        // Runs on after the caller has stored this task, so a failure always removes a stored entry.
        await Task.Yield();

        try
        {
            using IServiceScope scope = scopes.CreateScope();

            return await scope.ServiceProvider.GetRequiredKeyedService<IInstrumentCatalog>(InnerKey).ListAsync(category, CancellationToken.None);
        }
        catch
        {
            lock (_gate)
            {
                _cache.Remove(category);
            }

            throw;
        }
    }
}