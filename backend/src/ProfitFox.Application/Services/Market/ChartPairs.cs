using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Services.Market;

/// <summary>
///     The pairs of the requests of the chart of a pair and of its open interest, one for the process of the API (change
///     add-ideas-and-market, task 21.5): the whole list of instruments read for each request took more than half of the time
///     of a page of <c>5m</c> or <c>1h</c>. The list read is kept for <see cref="Keep" />; one read serves all, and the
///     requests that come while it is read wait for it. A pair the list kept does not have — listed since — reads the list
///     again. The scanner and «Рынок» read the catalog as before.
/// </summary>
public sealed class ChartPairs(IInstrumentCatalog catalog, TimeProvider time)
{
    /// <summary>
    ///     How long the list read is kept.
    /// </summary>
    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _reading = new(1, 1);
    private volatile Listed? _listed;

    /// <summary>
    ///     The pair of <paramref name="symbol" />, or null when the catalog does not list it.
    /// </summary>
    /// <exception cref="HistoryUnavailableException">The catalog did not answer.</exception>
    public async Task<Instrument?> Find(string symbol, CancellationToken cancellation)
    {
        Listed? kept = _listed;
        if (kept is not null && time.GetUtcNow() - kept.Read < Keep && kept.Pairs.TryGetValue(symbol, out Instrument? pair))
        {
            return pair;
        }

        Listed listed = await ReadAfter(kept, cancellation);
        return listed.Pairs.GetValueOrDefault(symbol);
    }

    // The list read after `seen`: the one another request read while this one waited, or a new read.
    private async Task<Listed> ReadAfter(Listed? seen, CancellationToken cancellation)
    {
        await _reading.WaitAsync(cancellation);
        try
        {
            if (_listed is { } current && !ReferenceEquals(current, seen))
            {
                return current;
            }

            DateTimeOffset read = time.GetUtcNow();
            IReadOnlyList<Instrument> instruments = await catalog.LinearUsdtPerpetuals(cancellation);
            var listed = new Listed(read, instruments.GroupBy(pair => pair.Symbol, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal));
            _listed = listed;
            return listed;
        }
        finally
        {
            _reading.Release();
        }
    }

    // The list of instruments by symbol, and when it was read.
    private sealed record Listed(DateTimeOffset Read, IReadOnlyDictionary<string, Instrument> Pairs);
}
