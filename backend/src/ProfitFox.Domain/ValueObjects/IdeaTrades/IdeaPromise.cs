using ProfitFox.Domain.Entities.Ideas.Enums;

namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>
///     What the idea promised when the trade was entered, kept as it was: the badge, the probability, the expectancy in R and the cases of its bucket
///     over 90 days (the badge and the probability none while the bucket has too few cases, all four none without a bucket), and the break-even and
///     the reward to risk after the fees, both from the entry price of the trade.
/// </summary>
public sealed record IdeaPromise(IdeaBadge? Badge, decimal? Probability, decimal? Expectancy, int? Cases, decimal? BreakEven, decimal NetRiskReward);