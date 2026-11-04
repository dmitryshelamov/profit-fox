using ProfitFox.Application.Ports.Market;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Scanner.Universe;

/// <summary>The pairs the scanner keeps: the USDT pairs of spot and the USDT perpetuals of linear that trade now.</summary>
public sealed class ScannerUniverse(IMarketDataFeed feed, IScannerPairRepository pairs, ScannerSettings settings, TimeProvider time)
{
    private const string Quote = "USDT";

    private DateTimeOffset? _refreshedAt;

    /// <summary>The instruments of <paramref name="listed" /> the scanner keeps; <see cref="ScannerSettings.Symbols" /> narrows them.</summary>
    public static IReadOnlyList<ListedInstrument> Select(IEnumerable<ListedInstrument> listed, ScannerSettings settings)
    {
        return listed
            .Where(instrument => instrument.Trading && instrument.QuoteCoin == Quote && (instrument.Category == MarketCategory.Spot || instrument.Perpetual))
            .Where(instrument => settings.Symbols.Count == 0 || settings.Symbols.Contains(instrument.Symbol, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    ///     The pairs that change once <paramref name="selected" /> is the list: a new pair, a pair back in trading, and a pair that is
    ///     no longer listed. A pair keeps its data either way.
    /// </summary>
    public static IReadOnlyList<ScannerPair> Merge(IReadOnlyList<ScannerPair> known, IReadOnlyList<ListedInstrument> selected)
    {
        var wanted = selected.Select(Instrument).ToHashSet();
        var knownInstruments = known.Select(pair => pair.Instrument).ToHashSet();

        return
        [
            .. known.Where(pair => pair.Listed != wanted.Contains(pair.Instrument)).Select(pair => pair with { Listed = !pair.Listed }),
            .. selected.Where(listed => !knownInstruments.Contains(Instrument(listed))).Select(listed => new ScannerPair(Instrument(listed), true, ListedAt: listed.ListedAt))
        ];
    }

    private static Instrument Instrument(ListedInstrument listed)
    {
        return new Instrument(listed.Category, listed.Symbol);
    }

    /// <summary>
    ///     Reads the list of the exchange once <see cref="ScannerSettings.UniverseRefresh" /> passed since the last read, saves the pairs
    ///     that change against <paramref name="known" /> and returns them; <c>null</c> when the list is not due yet.
    /// </summary>
    public async Task<IReadOnlyList<ScannerPair>?> RefreshAsync(IReadOnlyList<ScannerPair> known, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();

        if (_refreshedAt + settings.UniverseRefresh > now)
        {
            return null;
        }

        IReadOnlyList<ListedInstrument> spot = await feed.ListInstrumentsAsync(MarketCategory.Spot, cancellationToken);
        IReadOnlyList<ListedInstrument> linear = await feed.ListInstrumentsAsync(MarketCategory.Linear, cancellationToken);
        IReadOnlyList<ScannerPair> changed = Merge(known, Select([.. spot, .. linear], settings));
        await pairs.SaveAsync(changed, cancellationToken);
        _refreshedAt = now;

        return changed;
    }
}