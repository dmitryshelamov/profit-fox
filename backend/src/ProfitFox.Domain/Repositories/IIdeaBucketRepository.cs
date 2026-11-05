using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the statistics of the buckets, which the scanner computes and the API reads.</summary>
public interface IIdeaBucketRepository
{
    /// <summary>Replaces the buckets of the version with <paramref name="buckets" /> and removes the ones of other versions.</summary>
    Task ReplaceAsync(int engineVersion, IReadOnlyCollection<IdeaBucket> buckets, CancellationToken cancellationToken);

    Task<IReadOnlyList<IdeaBucket>> GetAsync(int engineVersion, CancellationToken cancellationToken);
}