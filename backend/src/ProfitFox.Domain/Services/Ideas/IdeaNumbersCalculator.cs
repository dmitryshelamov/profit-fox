using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The own numbers of an idea (decision 5 of add-own-idea-numbers) from its plan, the fees of its category, the statistics of its bucket over
///     90 days and the curve of «Уровни» of its set. A bucket without a case, or one stored before its expectancy without costs was counted, gives
///     the costs alone; one of «мало данных» an expectancy without an interval and no probability.
/// </summary>
public static class IdeaNumbersCalculator
{
    public static IdeaNumbers Compute(IdeaPlan plan, FeeRates fees, BucketStatistics? bucket, RiskRewardCurve? curve)
    {
        decimal costs = IdeaCosts.R(plan, fees);
        bool levels = plan.Type == IdeaType.Levels;
        decimal multiplier = levels ? RiskRewardCurve.MultiplierFor(curve, plan.RiskReward) : 1m;
        var costsOnly = new IdeaNumbers(costs, multiplier, null, null, null, null, null, null, null) { Band = levels ? RiskRewardBands.Of(plan.RiskReward) : null };

        if (bucket?.GrossExpectancy is not { } gross)
        {
            return costsOnly;
        }

        decimal expectancy = gross - costs;

        return costsOnly with
        {
            Probability = Capped(bucket.Probability * multiplier),
            ProbabilityLow = Capped(bucket.ProbabilityLow * multiplier),
            ProbabilityHigh = Capped(bucket.ProbabilityHigh * multiplier),
            Expectancy = expectancy,
            ExpectancyLow = bucket.GrossExpectancyLow - costs,
            ExpectancyHigh = bucket.GrossExpectancyHigh - costs,
            ExpectancyPerUsdt = expectancy * Math.Abs(plan.Entry - plan.Stop) / plan.Entry
        };
    }

    private static decimal? Capped(decimal? probability)
    {
        return probability is { } value ? Math.Min(1m, value) : null;
    }
}