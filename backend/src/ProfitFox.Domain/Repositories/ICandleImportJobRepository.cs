using ProfitFox.Domain.Entities.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Stores import jobs. A Worker runs a job under a lease, a pair of owner and expiry kept with the job: only one job of all Workers
///     holds a live lease at a time, and only its owner writes the job.
/// </summary>
public interface ICandleImportJobRepository
{
    Task AddAsync(CandleImportJob job, CancellationToken cancellationToken);

    Task<CandleImportJob?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Saves the job's status, progress, error and start/finish times, whoever holds its lease.</summary>
    Task UpdateAsync(CandleImportJob job, CancellationToken cancellationToken);

    /// <summary>
    ///     Leases to <paramref name="owner" /> until <paramref name="leasedUntil" /> the oldest job that is pending or running with an
    ///     expired lease, unless some job holds a live lease at <paramref name="now" />. Returns the job as stored, or <c>null</c>.
    /// </summary>
    Task<CandleImportJob?> ClaimNextAsync(Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken);

    /// <summary>Extends the lease of <paramref name="owner" />; <c>false</c> when the owner no longer holds it.</summary>
    Task<bool> RenewLeaseAsync(Guid id, Guid owner, DateTimeOffset leasedUntil, CancellationToken cancellationToken);

    /// <summary>
    ///     Saves the job like <see cref="UpdateAsync" /> if <paramref name="owner" /> still holds its lease, and ends the lease when the job
    ///     finished; <c>false</c> when the owner lost it.
    /// </summary>
    Task<bool> UpdateLeasedAsync(CandleImportJob job, Guid owner, CancellationToken cancellationToken);
}