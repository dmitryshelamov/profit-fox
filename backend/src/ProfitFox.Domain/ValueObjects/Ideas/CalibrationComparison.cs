namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The old model, the own numbers of a bucket without clustering or shrinking and from 30 own cases, against the new one, on the cases both
///     check (requirement "Сравнение старой и новой модели").
/// </summary>
public sealed record CalibrationComparison(CalibrationModel Old, CalibrationModel New);