namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     A run of the queue as its row says: what it is, where it is, who holds it and how far it has come.
/// </summary>
public sealed record RunRecord(
    string Id,
    string Hypothesis,
    int Version,
    string Period,
    bool Parity,
    string Status,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? LeaseOwner,
    DateTimeOffset? LeasedUntil,
    int LostLeases,
    bool CancelRequested,
    int PairsDone,
    int? PairsTotal);
