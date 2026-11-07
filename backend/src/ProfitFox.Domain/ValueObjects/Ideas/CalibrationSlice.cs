using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     A row of a breakdown of a report of calibration: the checked cases of an <see cref="Interval" /> or of a <see cref="Type" />, the other
///     none; the <see cref="Total" /> of all of them by promised expectancy, the row of those the model promised ▲, none without one, and the
///     comparison of the old model with the new one on them (requirement "Калибровка по ТФ и типам").
/// </summary>
public sealed record CalibrationSlice(Interval? Interval, IdeaType? Type, CalibrationRow Total, CalibrationRow? Up, CalibrationComparison Comparison);