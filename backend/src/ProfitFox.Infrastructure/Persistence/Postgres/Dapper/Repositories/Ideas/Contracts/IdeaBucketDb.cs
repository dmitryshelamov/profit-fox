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

    public int Targets { get; init; }

    public int Stops { get; init; }

    public int Expired { get; init; }

    public decimal? Probability { get; init; }

    public decimal? ProbabilityLow { get; init; }

    public decimal? ProbabilityHigh { get; init; }

    public decimal Expectancy { get; init; }

    public decimal? ExpectancyLow { get; init; }

    public decimal? ExpectancyHigh { get; init; }

    public int? Signals { get; init; }

    public decimal? OwnProbability { get; init; }

    public decimal? OwnProbabilityLow { get; init; }

    public decimal? OwnProbabilityHigh { get; init; }

    public decimal OwnExpectancy { get; init; }

    public decimal? OwnExpectancyLow { get; init; }

    public decimal? OwnExpectancyHigh { get; init; }

    public decimal? ParentWeight { get; init; }

    public bool FewOwnCases { get; init; }

    public decimal? GrossExpectancy { get; init; }

    public decimal? GrossExpectancyLow { get; init; }

    public decimal? GrossExpectancyHigh { get; init; }
}