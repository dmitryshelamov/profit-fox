using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The calibration of the cases fired in the last <see cref="Days" /> days of a <see cref="Category" />, all of them without one:
///     <see cref="Checked" /> cases compared with the statistics of their bucket at their firing, <see cref="Unchecked" /> ones whose bucket had
///     fewer than 30 cases then; the Brier score of the buckets and of the share of targets of the category, none without a checked case; the
///     rows by promised probability, by promised expectancy and by badge, only those with cases, and the <see cref="Total" /> of all checked ones
///     by promised expectancy.
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
    CalibrationRow? Total);