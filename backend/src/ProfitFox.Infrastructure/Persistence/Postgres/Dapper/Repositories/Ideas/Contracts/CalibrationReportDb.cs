using System.Text.Json.Serialization;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

/// <summary>The <c>report</c> of a row of <c>idea_calibration</c>; decimals are strings.</summary>
internal sealed class CalibrationReportDb
{
    public int Checked { get; init; }

    public int Unchecked { get; init; }

    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public decimal? Brier { get; init; }

    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public decimal? BaseBrier { get; init; }

    public required List<CalibrationRowDb> Probabilities { get; init; }

    public required List<CalibrationRowDb> Expectancies { get; init; }

    public required List<CalibrationRowDb> Badges { get; init; }

    public CalibrationRowDb? Total { get; init; }

    /// <summary>The table by own data; none in a report stored before honest-idea-intervals.</summary>
    public List<CalibrationOwnRowDb> Owns { get; init; } = [];

    public CalibrationComparisonDb? Comparison { get; init; }

    /// <summary>The breakdown by interval; none in a report stored before calibrate-ideas-by-timeframe.</summary>
    public List<CalibrationSliceDb> Intervals { get; init; } = [];

    /// <summary>The breakdown by type; none in a report stored before calibrate-ideas-by-timeframe.</summary>
    public List<CalibrationSliceDb> Types { get; init; } = [];

    /// <summary>The breakdown by feature; none in a report stored before add-market-idea-features.</summary>
    public List<CalibrationSliceDb> Features { get; init; } = [];
}