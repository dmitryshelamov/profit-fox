using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the pairs of the scanner, listed or not, with how much of their data storage holds.</summary>
public interface IScannerPairRepository
{
    /// <summary>Every pair the scanner ever kept.</summary>
    Task<IReadOnlyList<ScannerPair>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>The pair of an instrument, or <c>null</c> when the scanner never kept it.</summary>
    Task<ScannerPair?> GetAsync(Instrument instrument, CancellationToken cancellationToken);

    /// <summary>Adds the pairs or replaces the stored ones of the same instrument.</summary>
    Task SaveAsync(IReadOnlyCollection<ScannerPair> pairs, CancellationToken cancellationToken);
}