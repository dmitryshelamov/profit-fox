namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;

/// <summary>
///     A row of <c>backtests</c>, with the sum of its best set when the list reads it.
/// </summary>
internal sealed record BacktestDb
{
    public string Id { get; init; } = "";

    public Guid StrategyId { get; init; }

    public string StrategyName { get; init; } = "";

    public int StrategyVersion { get; init; }

    public string Kind { get; init; } = "";

    public string Graph { get; init; } = "{}";

    public string Settings { get; init; } = "{}";

    public int Sets { get; init; }

    public string? SourceId { get; init; }

    public int? SourceSet { get; init; }

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

    public int? EngineVersion { get; init; }

    public string? DataHash { get; init; }

    public double? ReadSeconds { get; init; }

    public double? ComputeSeconds { get; init; }

    public string? Reason { get; init; }

    public double? BestAmount { get; init; }
}
