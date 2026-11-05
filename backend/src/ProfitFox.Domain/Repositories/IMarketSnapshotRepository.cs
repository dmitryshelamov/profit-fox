using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the one market snapshot: a row per pair, replaced whole every minute; no history of snapshots.</summary>
public interface IMarketSnapshotRepository
{
    /// <summary>
    ///     Replaces the snapshot with <paramref name="rows" /> and records <paramref name="snapshotAt" /> in the state of the scanner, in one
    ///     transaction, if <paramref name="owner" /> still holds the lease of the stream; <c>false</c> when it lost it.
    /// </summary>
    Task<bool> ReplaceAsync(Guid owner, IReadOnlyCollection<MarketSnapshotRow> rows, DateTimeOffset snapshotAt, CancellationToken cancellationToken);

    Task<IReadOnlyList<MarketSnapshotRow>> GetAllAsync(CancellationToken cancellationToken);

    Task<MarketSnapshotRow?> GetAsync(Instrument instrument, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes the setups of the pairs into their rows of the snapshot, a pair not named with none, if <paramref name="owner" /> still holds
    ///     the lease of the stream; <c>false</c> when it lost it.
    /// </summary>
    Task<bool> ReplaceSetupsAsync(Guid owner, IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>> setups, CancellationToken cancellationToken);

    /// <summary>The setups of every pair of the snapshot that has some.</summary>
    Task<IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>>> GetSetupsAsync(CancellationToken cancellationToken);
}