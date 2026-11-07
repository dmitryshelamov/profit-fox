namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The Brier score of the model with features less that of the new model on the same cases (decision 4 of add-idea-features): the mean of the
///     paired differences with its 95 % interval over the clustered sums of the cases, none under 30 cases.
/// </summary>
public sealed record CalibrationDifference(decimal Mean, decimal? Low, decimal? High);