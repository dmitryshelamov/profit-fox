using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.Ideas;

/// <summary>Decision 3 of honest-idea-intervals: the numbers of a bucket are those of the model, its own ones go beside them.</summary>
public class BucketStatisticsTests
{
    // Scenario "Значок по модели": the own expectancy +0.30 R is reliably above zero, the model −0.10 R holds zero.
    [Test]
    public void Badge_goes_by_the_interval_of_the_model()
    {
        var statistics = new BucketStatistics(35, 14, 21, 0, 0.36m, 0.25m, 0.47m, -0.1m, -0.279m, 0.075m)
        {
            OwnProbability = 0.4m,
            OwnProbabilityLow = 0.25m,
            OwnProbabilityHigh = 0.56m,
            OwnExpectancy = 0.3m,
            OwnExpectancyLow = 0.02m,
            OwnExpectancyHigh = 0.58m,
            ParentWeight = 0.8m
        };

        statistics.Badge.ShouldBe(IdeaBadge.Flat);
    }
}