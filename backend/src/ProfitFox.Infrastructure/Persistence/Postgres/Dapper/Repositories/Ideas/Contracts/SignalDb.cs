namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

/// <summary>A signal of <c>idea_cases</c> with its path, or one the cursor of the replay keeps open.</summary>
internal sealed class SignalDb
{
    public required string Category { get; init; }

    public required string Symbol { get; init; }

    public required string Setup { get; init; }

    public required string Type { get; init; }

    public required string Interval { get; init; }

    public required string Side { get; init; }

    public DateTimeOffset FiredAt { get; init; }

    public int EngineVersion { get; init; }

    public decimal Entry { get; init; }

    public decimal Stop { get; init; }

    public decimal? Target { get; init; }

    public decimal Move { get; init; }

    public decimal VolumeRatio { get; init; }

    public decimal? PairChange { get; init; }

    public decimal? BtcChange { get; init; }

    public decimal? Funding { get; init; }

    public decimal Tick { get; init; }

    public int? EntryAfter { get; init; }

    public int? StopBeforeEntryAfter { get; init; }

    public int? StopAfter { get; init; }

    public required int?[] LadderAfter { get; init; }

    public int? TargetAfter { get; init; }

    public required decimal[] Closes { get; init; }

    public DateTimeOffset TrackedUntil { get; init; }
}