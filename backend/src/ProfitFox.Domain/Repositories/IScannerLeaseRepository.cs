using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     The lease of the scanner of ideas (change add-ideas-and-market, design.md, decision 1): one Worker of the bench keeps
///     the live data. Each call is one statement on the time of the database, as the lease of a backtest.
/// </summary>
public interface IScannerLeaseRepository
{
    /// <summary>
    ///     Takes the lease for <paramref name="worker" /> until the time of the database plus <paramref name="duration" />, or
    ///     renews it when the worker holds it; false when another Worker holds it and it has not run out.
    /// </summary>
    Task<bool> TakeAsync(string worker, TimeSpan duration, CancellationToken cancellationToken);

    /// <summary>
    ///     Gives the lease up, when the worker holds it.
    /// </summary>
    Task ReleaseAsync(string worker, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes what the holder tells of its work: since when it counts ideas without a break, its warmed and all pairs, and
    ///     how many pairs the warm-up failed on with the last reason, and how long the warm-up has still to go — null when it is
    ///     not known or over.
    /// </summary>
    Task ReportAsync(
        string worker,
        DateTimeOffset? liveSince,
        int warmed,
        int pairs,
        CancellationToken cancellationToken,
        int failed = 0,
        string? failure = null,
        TimeSpan? warmupLeft = null);

    /// <summary>
    ///     The lease as it is, with the time of the database; null before anyone took it.
    /// </summary>
    Task<(ScannerState State, DateTimeOffset Now)?> StateAsync(CancellationToken cancellationToken);
}
