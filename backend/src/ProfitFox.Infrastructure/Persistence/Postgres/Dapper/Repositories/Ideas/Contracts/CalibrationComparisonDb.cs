namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

/// <summary>
///     The old model against the new one and the model with features of a stored report of calibration; a report stored before the model with
///     features has none.
/// </summary>
internal sealed class CalibrationComparisonDb
{
    public required CalibrationModelDb Old { get; init; }

    public required CalibrationModelDb New { get; init; }

    public CalibrationModelDb? Features { get; init; }
}