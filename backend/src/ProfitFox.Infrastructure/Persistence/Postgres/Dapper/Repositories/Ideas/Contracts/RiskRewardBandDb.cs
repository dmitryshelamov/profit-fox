namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

internal sealed class RiskRewardBandDb
{
    public int EngineVersion { get; init; }

    public required string Interval { get; init; }

    public required string Side { get; init; }

    public required string Category { get; init; }

    public required string Band { get; init; }

    public int Cases { get; init; }

    public int Targets { get; init; }
}