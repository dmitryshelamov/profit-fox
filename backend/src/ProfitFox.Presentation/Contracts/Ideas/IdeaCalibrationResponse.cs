using System.Globalization;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.Ideas.Queries.GetIdeaCalibration;
using ProfitFox.Application.MarketData;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     The calibration of the cases fired in the last <see cref="Days" /> days of a <see cref="Category" />, all of them without one: when it
///     was computed (none before the first time, with zeros and empty tables), the checked cases and the ones whose bucket had fewer than 30
///     cases, the Brier score of the buckets and of the share of targets of the category, the rows by promised probability, by promised
///     expectancy and by badge, the <see cref="Total" /> of all checked cases, the rows by own data and the comparison of the old model with the
///     new one (none before the first calibration) and the breakdowns by interval and by type, all in a <see cref="Scope" />, <c>all</c> or
///     <c>fresh</c>. Numbers are decimal strings.
/// </summary>
public sealed record IdeaCalibrationResponse(
    DateTimeOffset? ComputedAt,
    int Days,
    string? Category,
    int Checked,
    int Unchecked,
    string? Brier,
    string? BaseBrier,
    IReadOnlyList<CalibrationRowResponse> Probabilities,
    IReadOnlyList<CalibrationRowResponse> Expectancies,
    IReadOnlyList<CalibrationRowResponse> Badges,
    CalibrationRowResponse? Total,
    string Scope,
    IReadOnlyList<CalibrationOwnRowResponse> Owns,
    CalibrationComparisonResponse? Comparison,
    IReadOnlyList<CalibrationSliceResponse> Intervals,
    IReadOnlyList<CalibrationSliceResponse> Types)
{
    public static IdeaCalibrationResponse Of(GetIdeaCalibrationResult result)
    {
        string? category = result.Category?.ToCode();
        string scope = result.Scope.ToCode();

        return result.Report is not { } report
            ? new IdeaCalibrationResponse(null, result.Days, category, 0, 0, null, null, [], [], [], null, scope, [], null, [], [])
            : new IdeaCalibrationResponse(
                report.ComputedAt,
                result.Days,
                category,
                report.Checked,
                report.Unchecked,
                Text(report.Brier),
                Text(report.BaseBrier),
                [.. report.Probabilities.Select(CalibrationRowResponse.Of)],
                [.. report.Expectancies.Select(CalibrationRowResponse.Of)],
                [.. report.Badges.Select(CalibrationRowResponse.Of)],
                report.Total is null ? null : CalibrationRowResponse.Of(report.Total),
                scope,
                [.. report.Owns.Select(CalibrationOwnRowResponse.Of)],
                report.Comparison is null ? null : CalibrationComparisonResponse.Of(report.Comparison),
                [.. report.Intervals.Select(CalibrationSliceResponse.Of)],
                [.. report.Types.Select(CalibrationSliceResponse.Of)]);
    }

    private static string? Text(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }
}