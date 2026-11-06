using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The calibration of the cases fired in the last <see cref="Days" /> days of a <see cref="Category" />, all of them without one, in a
///     <see cref="Scope" />: <see cref="Checked" /> cases compared with the model of their bucket at their firing, <see cref="Unchecked" /> ones
///     whose parent had fewer than 30 cases then; the Brier score of the buckets and of the share of targets of the category, none without a
///     checked case; the rows by promised probability, by promised expectancy and by badge, only those with cases, the <see cref="Total" /> of all
///     checked ones by promised expectancy, the rows by own data and the comparison of the old model with the new one (decision 5 of
///     honest-idea-intervals).
/// </summary>
public sealed record CalibrationReport(
    DateTimeOffset ComputedAt,
    int Days,
    MarketCategory? Category,
    int Checked,
    int Unchecked,
    decimal? Brier,
    decimal? BaseBrier,
    IReadOnlyList<CalibrationRow> Probabilities,
    IReadOnlyList<CalibrationRow> Expectancies,
    IReadOnlyList<CalibrationRow> Badges,
    CalibrationRow? Total)
{
    public CalibrationScope Scope { get; init; }

    public IReadOnlyList<CalibrationOwnRow> Owns { get; init; } = [];

    /// <summary>The old model against the new one; none in a report stored before it.</summary>
    public CalibrationComparison? Comparison { get; init; }

    /// <summary>The breakdown by interval; empty in a report stored before it.</summary>
    public IReadOnlyList<CalibrationSlice> Intervals { get; init; } = [];

    /// <summary>The breakdown by type; empty in a report stored before it.</summary>
    public IReadOnlyList<CalibrationSlice> Types { get; init; } = [];
}