using ProfitFox.Application.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

/// <summary>An idea with the statistics of its bucket, none unless the test gives them, and the money of an idea of entry 105, stop 100 and target 115.</summary>
public static class IdeaViewGenerator
{
    public static IdeaView Generate(Idea idea, BucketStatistics? bucket = null, IdeaShares? shares = null)
    {
        return new IdeaView(idea, bucket, null, shares ?? new IdeaShares(0.09m, 0.05m, 0.05m));
    }

    /// <summary>The statistics of a bucket of <paramref name="cases" /> cases: the baseline 0.4 and the interval of the probability 0.05 each side of it.</summary>
    public static BucketStatistics Bucket(decimal expectancy, decimal? probability = null, int cases = 400)
    {
        return new BucketStatistics(cases, 0, probability, probability - 0.05m, probability + 0.05m, 0.4m, expectancy);
    }
}