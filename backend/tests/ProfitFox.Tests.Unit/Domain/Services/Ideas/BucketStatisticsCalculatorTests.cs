using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>Requirement "Вероятность корзины".</summary>
public class BucketStatisticsCalculatorTests
{
    private const decimal Precision = 0.0001m;

    // Scenario "Корзина с краем": 180 targets of 380, Wilson 95 % from 42.40 % to 52.39 %, the baseline 1 / 3.
    [Test]
    public void Bucket_has_the_share_of_targets_with_its_interval_and_the_baseline()
    {
        BucketStatistics statistics = BucketStatisticsCalculator.Compute(180, 200, 20, 2m, 40m);

        (statistics.Cases, statistics.Expired).ShouldBe((400, 20));
        statistics.Probability!.Value.ShouldBe(0.4737m, Precision);
        statistics.ProbabilityLow!.Value.ShouldBe(0.4240m, Precision);
        statistics.ProbabilityHigh!.Value.ShouldBe(0.5239m, Precision);
        statistics.Baseline.ShouldBe(0.3333m, Precision);
        statistics.Expectancy.ShouldBe(0.1m);
        statistics.IsAboveBaseline.ShouldBeTrue();
    }

    [Test]
    public void Bucket_whose_interval_reaches_the_baseline_is_not_above_it()
    {
        BucketStatisticsCalculator.Compute(14, 26, 0, 2m, 0m).IsAboveBaseline.ShouldBeFalse();
    }

    // Scenario "Мало случаев".
    [Test]
    public void Bucket_of_fewer_than_thirty_cases_has_too_little_data_but_its_baseline()
    {
        BucketStatistics statistics = BucketStatisticsCalculator.Compute(6, 5, 1, 3m, 2.4m);

        (statistics.Probability, statistics.ProbabilityLow, statistics.ProbabilityHigh).ShouldBe((null, null, null));
        statistics.Baseline.ShouldBe(0.25m);
        statistics.Expectancy.ShouldBe(0.2m);
        statistics.IsAboveBaseline.ShouldBeFalse();
    }

    [Test]
    public void Bucket_whose_cases_all_expired_has_no_share()
    {
        BucketStatistics statistics = BucketStatisticsCalculator.Compute(0, 0, 30, 2m, -3m);

        (statistics.Probability, statistics.Expired, statistics.Expectancy).ShouldBe((null, 30, -0.1m));
    }
}