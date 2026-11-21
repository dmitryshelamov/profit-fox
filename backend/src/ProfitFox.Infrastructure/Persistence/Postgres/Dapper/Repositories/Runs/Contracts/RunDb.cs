namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs.Contracts;

/// <summary>
///     A row of <c>runs</c>.
/// </summary>
internal sealed record RunDb
{
    public string Id { get; init; } = "";

    public string Hypothesis { get; init; } = "";

    public int Version { get; init; }

    public string Period { get; init; } = "";

    public bool Parity { get; init; }

    public string Status { get; init; } = "";

    public DateTimeOffset QueuedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public string? LeaseOwner { get; init; }

    public DateTimeOffset? LeasedUntil { get; init; }

    public int LostLeases { get; init; }

    public bool CancelRequested { get; init; }

    public int PairsDone { get; init; }

    public int? PairsTotal { get; init; }
}
