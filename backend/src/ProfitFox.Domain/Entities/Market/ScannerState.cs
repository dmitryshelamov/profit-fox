using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Entities.Market;

/// <summary>
///     What the scanner tells of itself: when it saved the last snapshot, how many pairs have their history loaded whole, until when it sends no
///     REST request, and how far the replay of ideas has gone, counted from the cursors of the replay when the state is read (decision 4 of
///     speed-up-ideas-replay).
/// </summary>
public sealed record ScannerState(DateTimeOffset? SnapshotAt, int PairsTotal, int PairsBackfilled, DateTimeOffset? RestPausedUntil, IdeaReplayProgress? Replay = null);