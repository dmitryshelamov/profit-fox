namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

/// <summary>
///     The old model against the new one and the model with features of a stored report of calibration, and the difference of the Brier score of
///     the model with features from that of the new one; a report stored before the model with features has neither.
/// </summary>
internal sealed class CalibrationComparisonDb
{
    public required CalibrationModelDb Old { get; init; }

    public required CalibrationModelDb New { get; init; }

    public CalibrationModelDb? Features { get; init; }

    public CalibrationDifferenceDb? Difference { get; init; }
}