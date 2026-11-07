using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     A row of a breakdown of a report of calibration: the checked cases of an <see cref="Interval" />, of a <see cref="Type" />, or with or
///     without (<see cref="Present" />) a <see cref="Feature" />, the others none; the <see cref="Total" /> of all of them by promised
///     expectancy, the row of those the model promised ▲, none without one, and the comparison of the old model with the new one on them
///     (requirements "Калибровка по ТФ и типам" and "Калибровка по признакам").
/// </summary>
public sealed record CalibrationSlice(
    Interval? Interval,
    IdeaType? Type,
    CalibrationRow Total,
    CalibrationRow? Up,
    CalibrationComparison Comparison,
    IdeaFeature? Feature = null,
    bool? Present = null);