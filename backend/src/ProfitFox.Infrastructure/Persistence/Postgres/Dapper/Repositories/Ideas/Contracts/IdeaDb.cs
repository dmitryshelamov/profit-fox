namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

/// <summary>A row of <c>ideas</c>.</summary>
internal sealed class IdeaDb
{
    public Guid Id { get; init; }

    public required string Category { get; init; }

    public required string Symbol { get; init; }

    public required string Setup { get; init; }

    public required string Type { get; init; }

    public required string Interval { get; init; }

    public required string Side { get; init; }

    public DateTimeOffset FiredAt { get; init; }

    public decimal Entry { get; init; }

    public decimal Stop { get; init; }

    public decimal Target { get; init; }

    public decimal RiskReward { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset StatusAt { get; init; }

    public DateTimeOffset? EnteredAt { get; init; }

    public decimal? ExitPrice { get; init; }

    public decimal Move { get; init; }

    public decimal VolumeRatio { get; init; }

    public decimal? PairChange { get; init; }

    public decimal? BtcChange { get; init; }

    public decimal? Funding { get; init; }

    public int? BurstPairs { get; init; }

    public int EngineVersion { get; init; }

    public decimal? Tick { get; init; }

    public int LifeCandles { get; init; }

    public decimal? TargetR { get; init; }

    public int? EntryCandles { get; init; }

}