namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The old model, the own numbers of a bucket without clustering or shrinking and from 30 own cases, against the new one and the model with
///     features, on the cases all three check (requirement "Сравнение старой и новой модели"), and the Brier score of the model with features less
///     that of the new one, none without a case; a report stored before the model with features has none of either.
/// </summary>
public sealed record CalibrationComparison(CalibrationModel Old, CalibrationModel New, CalibrationModel? Features = null, CalibrationDifference? Difference = null);