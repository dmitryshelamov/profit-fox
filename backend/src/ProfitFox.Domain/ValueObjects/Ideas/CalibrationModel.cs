namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     How a model of the buckets did on the cases of a comparison: their number, the Brier score of their promised probability, none without a
///     case, and the row of those it promised ▲, none without one.
/// </summary>
public sealed record CalibrationModel(int Cases, decimal? Brier, CalibrationRow? Up);