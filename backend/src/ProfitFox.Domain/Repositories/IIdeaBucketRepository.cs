using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the statistics of the buckets, which the scanner computes and the API reads.</summary>
public interface IIdeaBucketRepository
{
    /// <summary>
    ///     Replaces the buckets of the version with <paramref name="buckets" /> and removes the ones of other versions; with <paramref name="only" />,
    ///     only the buckets of that type × interval.
    /// </summary>
    Task ReplaceAsync(int engineVersion, IReadOnlyCollection<IdeaBucket> buckets, (IdeaType Type, Interval Interval)? only, CancellationToken cancellationToken);

    Task<IReadOnlyList<IdeaBucket>> GetAsync(int engineVersion, CancellationToken cancellationToken);
}