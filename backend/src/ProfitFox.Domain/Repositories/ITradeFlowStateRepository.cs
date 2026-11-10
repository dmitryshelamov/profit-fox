namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the lease of the trade flow in one row, which one collector process holds at a time.</summary>
public interface ITradeFlowStateRepository
{
    /// <summary>
    ///     Leases the trade flow to <paramref name="owner" /> until <paramref name="leasedUntil" /> unless another collector holds a live lease at
    ///     <paramref name="now" />; <c>false</c> when it does.
    /// </summary>
    Task<bool> ClaimAsync(Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken);

    /// <summary>Extends the lease of <paramref name="owner" />; <c>false</c> when the owner no longer holds it.</summary>
    Task<bool> RenewAsync(Guid owner, DateTimeOffset leasedUntil, CancellationToken cancellationToken);
}