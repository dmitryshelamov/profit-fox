using ProfitFox.Domain.Entities.Ideas.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>How a case of the replay ended, as the calibration reads it: its bucket, when it fired and closed, its outcome and its result in R.</summary>
public sealed record IdeaOutcome(IdeaBucketKey Key, DateTimeOffset FiredAt, DateTimeOffset StatusAt, IdeaStatus Status, decimal Result);