namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

/// <summary>A row of a breakdown of a stored report of calibration: the code of its interval or of its type, the other none.</summary>
internal sealed class CalibrationSliceDb
{
    public string? Interval { get; init; }

    public string? Type { get; init; }

    public required CalibrationRowDb Total { get; init; }

    public CalibrationRowDb? Up { get; init; }

    public required CalibrationComparisonDb Comparison { get; init; }
}