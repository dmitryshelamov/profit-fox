namespace ProfitFox.Bench.Presentation.Runs;

/// <summary>
///     A run of the bench (spec bench-run-queue). <see cref="State" /> is <c>queued</c>, <c>running</c> or <c>finished</c>;
///     <see cref="Kind" /> is <c>verdict</c> or <c>parity</c>. A run that goes has its <see cref="Worker" />, the pairs it has
///     passed of the pairs it will pass, and when it started; a run that ended has its <see cref="Result" />. A line of the list of finished runs has no times of the queue.
/// </summary>
public sealed record RunResponse(
    string Id,
    string Hypothesis,
    int Version,
    string Period,
    string Kind,
    string State,
    DateTimeOffset? QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Worker,
    int PairsDone,
    int? PairsTotal,
    int LostLeases,
    bool CancelRequested,
    RunResultResponse? Result);
