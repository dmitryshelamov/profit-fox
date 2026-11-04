namespace ProfitFox.Domain.Entities.Market;

/// <summary>What the scanner tells of itself: when it saved the last snapshot, how many pairs have their history loaded whole, and until when it sends no REST request.</summary>
public sealed record ScannerState(DateTimeOffset? SnapshotAt, int PairsTotal, int PairsBackfilled, DateTimeOffset? RestPausedUntil);