using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     The lease of the trading Worker (change take-idea-trades, design.md, decision 2): one Worker of the bench sends orders
///     and leads the trades. Each call is one statement on the time of the database, as the lease of the scanner.
/// </summary>
public interface ITraderLeaseRepository
{
    /// <summary>
    ///     Takes the lease for <paramref name="worker" /> or renews it; false when another Worker holds it and it has not run out.
    /// </summary>
    Task<bool> TakeAsync(string worker, TimeSpan duration, CancellationToken cancellationToken);

    Task ReleaseAsync(string worker, CancellationToken cancellationToken);

    /// <summary>
    ///     The lease as it is with the time of the database; null before anyone took it.
    /// </summary>
    Task<TraderState?> StateAsync(CancellationToken cancellationToken);
}
