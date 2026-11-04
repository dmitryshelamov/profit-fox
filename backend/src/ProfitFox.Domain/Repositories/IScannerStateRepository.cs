using ProfitFox.Domain.Entities.Market;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Stores the state of the scanner in one row: the lease of the stream, which one scanner process holds at a time, and what the
///     API shows of the scanner.
/// </summary>
public interface IScannerStateRepository
{
    /// <summary>
    ///     Leases the stream to <paramref name="owner" /> until <paramref name="leasedUntil" /> unless another scanner holds a live lease at
    ///     <paramref name="now" />; <c>false</c> when it does.
    /// </summary>
    Task<bool> ClaimAsync(Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken);

    /// <summary>Extends the lease of <paramref name="owner" />; <c>false</c> when the owner no longer holds it.</summary>
    Task<bool> RenewAsync(Guid owner, DateTimeOffset leasedUntil, CancellationToken cancellationToken);

    /// <summary>Records that the scanner sends no REST request until <paramref name="until" />.</summary>
    Task PauseRestAsync(DateTimeOffset until, CancellationToken cancellationToken);

    Task<ScannerState> GetAsync(CancellationToken cancellationToken);

    /// <summary>Records how many listed pairs the scanner keeps and how many of them have their history loaded whole.</summary>
    Task SetProgressAsync(int pairsTotal, int pairsBackfilled, CancellationToken cancellationToken);
}