using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     The snapshot of the market (change add-ideas-and-market, design.md, decision 8), written by the scanner and read by
///     «Рынок».
/// </summary>
public interface IMarketPairRepository
{
    /// <summary>
    ///     Writes the snapshot of every pair in one statement; a pair that is not in it any more goes.
    /// </summary>
    Task ReplaceAsync(IReadOnlyList<MarketPair> pairs, CancellationToken cancellationToken);

    /// <summary>
    ///     A page of the pairs that match the query, sorted by a field of <see cref="Services.Lists.ListFields.Market" />; a
    ///     pair without a value of that field goes after the others (spec market-overview «Список пар рынка»).
    /// </summary>
    Task<ListPage<MarketPairRow>> ListAsync(MarketListQuery query, CancellationToken cancellationToken);

    Task<MarketPairRow?> FindAsync(string symbol, CancellationToken cancellationToken);

    /// <summary>
    ///     How many pairs and open ideas match the conditions, and how many there are.
    /// </summary>
    Task<FilterCount> CountAsync(IReadOnlyList<FilterCondition> conditions, CancellationToken cancellationToken);
}
