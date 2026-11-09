using ProfitFox.Application.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

/// <summary>
///     An idea with the statistics of its bucket, none unless the test gives them, the money of an idea of entry 105, stop 100 and target 115, and
///     its own numbers: those the test gives, by default those of its bucket without costs.
/// </summary>
public static class IdeaViewGenerator
{
    public static IdeaView Generate(Idea idea, BucketStatistics? bucket = null, IdeaShares? shares = null, IdeaNumbers? numbers = null)
    {
        IdeaShares money = shares ?? new IdeaShares(0.09m, 0.05m, 0.05m);

        return new IdeaView(idea, bucket, null, money, numbers ?? new IdeaNumbers(0m, 1m, bucket?.Probability, bucket?.ProbabilityLow, bucket?.ProbabilityHigh,
            bucket?.Expectancy, bucket?.ExpectancyLow, bucket?.ExpectancyHigh, bucket?.Expectancy * money.RiskPerUsdt));
    }

    /// <summary>The own numbers of an idea with the expectancy, its interval and the expectancy on 1 USDT the test gives.</summary>
    public static IdeaNumbers Numbers(decimal? expectancy, decimal? perUsdt = null, decimal? low = null, decimal? high = null, decimal? probability = null)
    {
        return new IdeaNumbers(0m, 1m, probability, probability, probability, expectancy, low, high, perUsdt);
    }

    /// <summary>
    ///     The statistics of a bucket of <paramref name="cases" /> cases: the interval of the probability 0.05 each side of it, and the interval
    ///     of the expectancy the test gives, none by default.
    /// </summary>
    public static BucketStatistics Bucket(decimal expectancy, decimal? probability = null, int cases = 400, decimal? expectancyLow = null, decimal? expectancyHigh = null)
    {
        return new BucketStatistics(cases, 0, 0, 0, probability, probability - 0.05m, probability + 0.05m, expectancy, expectancyLow, expectancyHigh);
    }
}