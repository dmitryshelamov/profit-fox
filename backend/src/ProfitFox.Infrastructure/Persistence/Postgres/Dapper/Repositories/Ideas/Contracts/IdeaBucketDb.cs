namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

internal sealed class IdeaBucketDb
{
    public int EngineVersion { get; init; }

    public int Days { get; init; }

    public required string Setup { get; init; }

    public required string Type { get; init; }

    public required string Interval { get; init; }

    public required string Side { get; init; }

    public required string Category { get; init; }

    public int Cases { get; init; }

    public int Expired { get; init; }

    public decimal? Probability { get; init; }

    public decimal? ProbabilityLow { get; init; }

    public decimal? ProbabilityHigh { get; init; }

    public decimal Baseline { get; init; }

    public decimal Expectancy { get; init; }
}