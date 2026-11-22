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
}
