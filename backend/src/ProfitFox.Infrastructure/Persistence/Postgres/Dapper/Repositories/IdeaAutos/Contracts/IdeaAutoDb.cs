namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Contracts;

internal sealed class IdeaAutoDb
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public Guid AccountId { get; init; }

    public decimal Amount { get; init; }

    public required string Mode { get; init; }

    public required string[] Setups { get; init; }

    public required string[] Types { get; init; }

    public required string[] Intervals { get; init; }

    public string? Side { get; init; }

    public string? Category { get; init; }

    public string? Badge { get; init; }

    public decimal MinRiskReward { get; init; }

    public int MinCases { get; init; }

    public required string DayDrop { get; init; }

    public required string BtcDrop { get; init; }

    public required string HighFunding { get; init; }

    public required string Burst { get; init; }

    public int Position { get; init; }

    public DateTimeOffset? DeletedAt { get; init; }

    public decimal? Budget { get; init; }

    public int? MaxPerMinutePerSide { get; init; }

    public int? MaxOpenPerSide { get; init; }
}