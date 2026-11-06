using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>Requirement "Вероятность корзины".</summary>
public class BucketStatisticsCalculatorTests
{
    private const decimal Precision = 0.0001m;

    // Scenario "Корзина с краем": 180 targets of all 400 cases, not of the 380 targets and stops; Wilson 95 % from 40.19 % to 49.90 %.
    [Test]
    public void Bucket_has_the_share_of_targets_among_all_cases_with_its_interval()
    {
        BucketStatistics statistics = BucketStatisticsCalculator.Compute(180, 200, 20, 40m, 400m);

        (statistics.Cases, statistics.Targets, statistics.Stops, statistics.Expired).ShouldBe((400, 180, 200, 20));
        statistics.Probability.ShouldBe(0.45m);
        statistics.ProbabilityLow!.Value.ShouldBe(0.4019m, Precision);
        statistics.ProbabilityHigh!.Value.ShouldBe(0.4990m, Precision);
        statistics.Expectancy.ShouldBe(0.1m);
    }

    // Scenario "Ожидание надёжно выше нуля": the mean +0.12 R, s 1.2 R of 400 cases, ΣR² = s² · 399 + (ΣR)² / 400 = 574.56 + 5.76.
    [Test]
    public void Bucket_has_the_95_percent_interval_of_its_mean_result()
    {
        BucketStatistics statistics = BucketStatisticsCalculator.Compute(180, 200, 20, 48m, 580.32m);

        statistics.Expectancy.ShouldBe(0.12m);
        statistics.ExpectancyLow!.Value.ShouldBe(0.0024m, Precision);
        statistics.ExpectancyHigh!.Value.ShouldBe(0.2376m, Precision);
        statistics.Badge.ShouldBe(IdeaBadge.Up);
    }

    // Scenario "Ожидание не отличается от нуля": the mean +0.05 R, s 1.2 R, ΣR² = 574.56 + 1.
    [Test]
    public void Bucket_whose_interval_holds_zero_is_flat()
    {
        BucketStatistics statistics = BucketStatisticsCalculator.Compute(180, 200, 20, 20m, 575.56m);

        statistics.ExpectancyLow!.Value.ShouldBe(-0.0676m, Precision);
        statistics.ExpectancyHigh!.Value.ShouldBe(0.1676m, Precision);
        statistics.Badge.ShouldBe(IdeaBadge.Flat);
    }

    [Test]
    public void Bucket_whose_interval_is_all_below_zero_is_down()
    {
        BucketStatisticsCalculator.Compute(180, 200, 20, -48m, 580.32m).Badge.ShouldBe(IdeaBadge.Down);
    }

    // Sums rounded to 12 places may leave the variance of equal results a hair below zero: the interval closes on the mean.
    [Test]
    public void Bucket_of_equal_results_has_its_interval_at_the_mean()
    {
        BucketStatistics statistics = BucketStatisticsCalculator.Compute(0, 30, 0, -30m, 29.999999999999m);

        (statistics.ExpectancyLow, statistics.ExpectancyHigh).ShouldBe((-1m, -1m));
    }

    // Scenario "Мало случаев".
    [Test]
    public void Bucket_of_fewer_than_thirty_cases_has_too_little_data_but_its_counts_and_expectancy()
    {
        BucketStatistics statistics = BucketStatisticsCalculator.Compute(6, 5, 1, 2.4m, 20m);

        (statistics.Cases, statistics.Targets, statistics.Stops, statistics.Expired).ShouldBe((12, 6, 5, 1));
        (statistics.Probability, statistics.ProbabilityLow, statistics.ProbabilityHigh).ShouldBe((null, null, null));
        (statistics.ExpectancyLow, statistics.ExpectancyHigh).ShouldBe((null, null));
        statistics.Expectancy.ShouldBe(0.2m);
        statistics.Badge.ShouldBeNull();
    }

    [Test]
    public void Bucket_whose_cases_all_expired_has_no_target_in_its_share()
    {
        BucketStatistics statistics = BucketStatisticsCalculator.Compute(0, 0, 30, -3m, 0.3m);

        (statistics.Probability, statistics.Expired, statistics.Expectancy).ShouldBe((0m, 30, -0.1m));
    }
}