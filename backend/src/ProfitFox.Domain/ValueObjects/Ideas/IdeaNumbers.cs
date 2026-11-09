using ProfitFox.Domain.Entities.Ideas.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The own numbers of an idea (decision 5 of add-own-idea-numbers), which take the place of those of its bucket wherever an idea is shown,
///     filtered, sorted, taken by an auto or promised: its <see cref="Costs" /> in R; the probability of its bucket over 90 days with its interval,
///     times the <see cref="Multiplier" /> of the band of its reward to risk for «Уровни» (1 for the other types) and never above 1; the
///     expectancy without costs of the bucket less the costs, with its interval shifted by them and the <see cref="Badge" /> by it; and the
///     expectancy on 1 USDT of the amount. What an idea does not have is none.
/// </summary>
public sealed record IdeaNumbers(
    decimal Costs,
    decimal Multiplier,
    decimal? Probability,
    decimal? ProbabilityLow,
    decimal? ProbabilityHigh,
    decimal? Expectancy,
    decimal? ExpectancyLow,
    decimal? ExpectancyHigh,
    decimal? ExpectancyPerUsdt)
{
    /// <summary>The band of the reward to risk of the plan of «Уровни» the multiplier is of; none for the other types.</summary>
    public RiskRewardBand? Band { get; init; }

    public IdeaBadge? Badge => IdeaBadges.Of(ExpectancyLow, ExpectancyHigh);
}