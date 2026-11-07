namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

/// <summary>The Brier score of the model with features less that of the new one in a stored report, with its interval.</summary>
internal sealed class CalibrationDifferenceDb
{
    public decimal Mean { get; init; }

    public decimal? Low { get; init; }

    public decimal? High { get; init; }
}