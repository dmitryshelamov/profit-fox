using ProfitFox.Domain.Entities.Ideas.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     How a case of the replay ended, as the calibration reads it: its bucket, when it fired and closed, its outcome and its result in R, and the
///     features of its signal, the level of its bucket the model with features checks it by.
/// </summary>
public sealed record IdeaOutcome(IdeaBucketKey Key, DateTimeOffset FiredAt, DateTimeOffset StatusAt, IdeaStatus Status, decimal Result, IdeaFeatures Features = default);