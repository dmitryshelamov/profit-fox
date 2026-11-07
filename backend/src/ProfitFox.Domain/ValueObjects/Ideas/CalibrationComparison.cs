namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The old model, the own numbers of a bucket without clustering or shrinking and from 30 own cases, against the new one and the model with
///     features, on the cases all three check (requirement "Сравнение старой и новой модели"); a report stored before the model with features has
///     none of it.
/// </summary>
public sealed record CalibrationComparison(CalibrationModel Old, CalibrationModel New, CalibrationModel? Features = null);