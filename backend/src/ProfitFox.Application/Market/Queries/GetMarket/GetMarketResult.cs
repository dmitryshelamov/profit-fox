using ProfitFox.Application.Paging;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Market.Queries.GetMarket;

/// <summary>
///     The page of rows of the snapshot that meet the query (none with <see cref="Errors" />), the setups of the pairs that have some, the
///     counts for the screen and the state of the scanner; a pause of REST that has ended is none.
/// </summary>
public sealed record GetMarketResult(
    ScannerState State,
    Page<MarketSnapshotRow>? Page,
    IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>> Setups,
    MarketCounts? Counts,
    IReadOnlyDictionary<string, string> Errors);

/// <summary>
///     The pairs of the snapshot, and of each category in the whole snapshot; the pairs left after the category, the search and the saved
///     filter, and how many of them hold each setup on the interval asked.
/// </summary>
public sealed record MarketCounts(int Pairs, IReadOnlyDictionary<MarketCategory, int> Categories, int Filtered, IReadOnlyDictionary<IdeaSetup, int> Setups);