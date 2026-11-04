using ProfitFox.Domain.Entities.Market;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the pairs of the scanner, listed or not, with how much of their data storage holds.</summary>
public interface IScannerPairRepository
{
    /// <summary>Every pair the scanner ever kept.</summary>
    Task<IReadOnlyList<ScannerPair>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Adds the pairs or replaces the stored ones of the same instrument.</summary>
    Task SaveAsync(IReadOnlyCollection<ScannerPair> pairs, CancellationToken cancellationToken);
}