using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.MarketData.Queries.SearchInstruments;

/// <summary>The filter and the order of an instrument search.</summary>
public static class InstrumentSearch
{
    /// <summary>
    ///     Instruments whose symbol, base coin, quote coin or name contain the query, ignoring case: symbols that start with the query
    ///     first, then the rest, each part ordered by symbol ordinally, cut to <paramref name="limit" />.
    /// </summary>
    public static IReadOnlyList<ListedInstrument> Find(IEnumerable<ListedInstrument> instruments, string query, int limit)
    {
        return instruments
            .Where(instrument => Contains(instrument.Symbol, query)
                                 || Contains(instrument.BaseCoin, query)
                                 || Contains(instrument.QuoteCoin, query)
                                 || (instrument.Name is { } name && Contains(name, query)))
            .OrderBy(instrument => instrument.Symbol.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(instrument => instrument.Symbol, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    private static bool Contains(string value, string query)
    {
        return value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}