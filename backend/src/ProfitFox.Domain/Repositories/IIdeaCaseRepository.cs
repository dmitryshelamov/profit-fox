using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the cases of the replay of history (decisions 5 to 7).</summary>
public interface IIdeaCaseRepository
{
    /// <summary>Adds the cases; a case already stored under its key, fired time and version stays as it is.</summary>
    Task AddAsync(IReadOnlyCollection<IdeaCase> cases, CancellationToken cancellationToken);

    /// <summary>The cases of a pair of a version, oldest fired first.</summary>
    Task<IReadOnlyList<IdeaCase>> GetAsync(Instrument instrument, int engineVersion, CancellationToken cancellationToken);

    /// <summary>
    ///     The cases of a version fired since <paramref name="from" /> counted by bucket, only those whose move and volume pass the thresholds
    ///     of their interval: impulses and drops the impulse ones, early setups the early ones; high volatility passes as it is.
    /// </summary>
    Task<IReadOnlyList<IdeaBucketCounts>> CountAsync(
        int engineVersion,
        DateTimeOffset from,
        IReadOnlyDictionary<Interval, SetupThresholds> thresholds,
        CancellationToken cancellationToken);

    /// <summary>Removes the cases of every other version.</summary>
    Task DeleteOtherVersionsAsync(int engineVersion, CancellationToken cancellationToken);
}