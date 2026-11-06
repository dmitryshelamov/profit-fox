namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

/// <summary>The old model against the new one of a stored report of calibration.</summary>
internal sealed class CalibrationComparisonDb
{
    public required CalibrationModelDb Old { get; init; }

    public required CalibrationModelDb New { get; init; }
}