using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>Decision 1 and requirement "Скученность случаев по времени": the sums of the cases of a group by the hours they fired in.</summary>
public class ClusteredSumsTests
{
    private const decimal Precision = 0.0001m;

    private static readonly DateTimeOffset Hour = DateTimeOffset.Parse("2024-06-01T10:00:00Z");
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    // Two cases of one hour are one group of two: n_g² = 4, n_g·S_g = 2 · 1, S_g² = 1, n_g·T_g = 2, T_g² = 1.
    [Test]
    public void Cases_of_one_hour_are_one_group()
    {
        var sums = new ClusteredSums(OneHour);

        sums.Add(Hour, IdeaStatus.Target, 2m);
        sums.Add(Hour.AddMinutes(59), IdeaStatus.Stop, -1m);

        Totals(sums).ShouldBe((2, 1, 1, 0, 1m, 5m, 1, 4L, 2m, 1m, 2L, 1L));
    }

    // Two cases of two hours are two groups of one: n_g² = 1 + 1, n_g·S_g = 2 − 1, S_g² = 4 + 1, n_g·T_g = 1, T_g² = 1.
    [Test]
    public void Cases_of_two_hours_are_two_groups()
    {
        var sums = new ClusteredSums(OneHour);

        sums.Add(Hour, IdeaStatus.Target, 2m);
        sums.Add(Hour.AddHours(1), IdeaStatus.Stop, -1m);

        Totals(sums).ShouldBe((2, 1, 1, 0, 1m, 5m, 2, 2L, 1m, 5m, 1L, 1L));
    }

    // Scenario "Окно входа удлиняет блок": blocks of 28 hours run from the Unix epoch, so one of them is 31 May 04:00 to 1 June 08:00 UTC.
    [Test]
    public void Cases_of_one_block_of_the_holding_are_one_group()
    {
        var sums = new ClusteredSums(TimeSpan.FromHours(28));

        sums.Add(DateTimeOffset.Parse("2024-05-31T04:00:00Z"), IdeaStatus.Target, 2m);
        sums.Add(DateTimeOffset.Parse("2024-06-01T07:59:00Z"), IdeaStatus.Stop, -1m);
        int oneBlock = sums.Groups;
        sums.Add(DateTimeOffset.Parse("2024-06-01T08:00:00Z"), IdeaStatus.Stop, -1m);

        (oneBlock, sums.Groups).ShouldBe((1, 2));
    }

    // Scenarios "Одно движение рынка" and "Доля при одном движении" on 4 h: 20 blocks of 4 days from 29 May 2024, each with 20 cases in
    // different hours and equal results, 8 blocks of +2 R targets and 12 of −1 R stops. By the hour they would be 400 independent cases,
    // +0.056…+0.344 R; by the holding they are 20 bets.
    [Test]
    public void Cases_of_one_block_that_moved_as_one_widen_the_intervals()
    {
        var sums = new ClusteredSums(TimeSpan.FromDays(4));
        var start = DateTimeOffset.Parse("2024-05-29T00:00:00Z");

        for (int block = 0; block < 20; block++)
        {
            for (int hour = 0; hour < 20; hour++)
            {
                sums.Add(start.AddDays(4 * block).AddHours(hour * 4.5), block < 8 ? IdeaStatus.Target : IdeaStatus.Stop, block < 8 ? 2m : -1m);
            }
        }

        BucketStatistics statistics = sums.Statistics();

        statistics.Expectancy.ShouldBe(0.2m);
        statistics.ExpectancyLow!.Value.ShouldBe(-0.4608m, Precision);
        statistics.ExpectancyHigh!.Value.ShouldBe(0.8608m, Precision);
        statistics.Probability.ShouldBe(0.4m);
        statistics.ProbabilityLow!.Value.ShouldBe(0.2152m, Precision);
        statistics.ProbabilityHigh!.Value.ShouldBe(0.6184m, Precision);
    }

    [Test]
    public void Removing_a_case_gives_back_the_sums_before_it_was_added()
    {
        var sums = new ClusteredSums(OneHour);
        sums.Add(Hour, IdeaStatus.Target, 2m);
        sums.Add(Hour.AddHours(1), IdeaStatus.Expired, 0.3m);
        (int, int, int, int, decimal, decimal, int, long, decimal, decimal, long, long) before = Totals(sums);

        sums.Add(Hour.AddMinutes(5), IdeaStatus.Stop, -1m);
        sums.Add(Hour.AddHours(2), IdeaStatus.Target, 2m);
        sums.Remove(Hour.AddMinutes(5), IdeaStatus.Stop, -1m);
        sums.Remove(Hour.AddHours(2), IdeaStatus.Target, 2m);

        Totals(sums).ShouldBe(before);
    }

    // Scenarios "Одно движение рынка" and "Доля при одном движении": 20 hours of 20 equal cases, 8 of them +2 R targets, 12 −1 R stops. The
    // groups are 20 cases that moved as one, so the interval is that of 20 results, not of 400; the share is as wide as of 19 independent cases.
    [Test]
    public void Cases_of_one_hour_that_moved_as_one_widen_the_intervals()
    {
        var sums = new ClusteredSums(OneHour);

        for (int hour = 0; hour < 20; hour++)
        {
            for (int index = 0; index < 20; index++)
            {
                sums.Add(Hour.AddHours(hour).AddMinutes(index), hour < 8 ? IdeaStatus.Target : IdeaStatus.Stop, hour < 8 ? 2m : -1m);
            }
        }

        BucketStatistics statistics = sums.Statistics();

        statistics.Expectancy.ShouldBe(0.2m);
        statistics.ExpectancyLow!.Value.ShouldBe(-0.4608m, Precision);
        statistics.ExpectancyHigh!.Value.ShouldBe(0.8608m, Precision);
        statistics.Probability.ShouldBe(0.4m);
        statistics.ProbabilityLow!.Value.ShouldBe(0.2152m, Precision);
        statistics.ProbabilityHigh!.Value.ShouldBe(0.6184m, Precision);
    }

    // Scenario "Ожидание надёжно выше нуля": 400 cases each in its own hour, half +1.3185 R and half −1.0785 R, the mean +0.12 R and s 1.2 R.
    [Test]
    public void Cases_each_in_its_own_hour_have_the_interval_of_independent_cases()
    {
        var sums = new ClusteredSums(OneHour);

        for (int index = 0; index < 400; index++)
        {
            sums.Add(Hour.AddHours(index), index % 2 == 0 ? IdeaStatus.Target : IdeaStatus.Stop, index % 2 == 0 ? 1.3185m : -1.0785m);
        }

        BucketStatistics statistics = sums.Statistics();

        statistics.Expectancy.ShouldBe(0.12m);
        statistics.ExpectancyLow!.Value.ShouldBe(0.0024m, Precision);
        statistics.ExpectancyHigh!.Value.ShouldBe(0.2376m, Precision);
    }

    // Scenario "Скученность не сужает": every hour has a +2 R target and a −1 R stop, so the hours do not differ at all; the intervals are
    // those of 60 independent cases.
    [Test]
    public void Hours_that_differ_less_than_independent_cases_keep_the_independent_intervals()
    {
        var sums = new ClusteredSums(OneHour);

        for (int hour = 0; hour < 30; hour++)
        {
            sums.Add(Hour.AddHours(hour), IdeaStatus.Target, 2m);
            sums.Add(Hour.AddHours(hour), IdeaStatus.Stop, -1m);
        }

        BucketStatistics statistics = sums.Statistics();

        BucketStatistics independent = BucketStatisticsCalculator.Compute(30, 30, 0, 30m, 150m);
        statistics.ExpectancyLow!.Value.ShouldBe(independent.ExpectancyLow!.Value, Precision);
        statistics.ExpectancyHigh!.Value.ShouldBe(independent.ExpectancyHigh!.Value, Precision);
        statistics.ProbabilityLow!.Value.ShouldBe(independent.ProbabilityLow!.Value, Precision);
        statistics.ProbabilityHigh!.Value.ShouldBe(independent.ProbabilityHigh!.Value, Precision);
    }

    // Decision 1: in one hour all cases are one: the interval of the mean is ± 1.96 s, the share is as wide as of one case.
    [Test]
    public void Cases_all_of_one_hour_have_the_intervals_of_one_case()
    {
        var sums = new ClusteredSums(OneHour);

        for (int index = 0; index < 30; index++)
        {
            sums.Add(Hour.AddMinutes(index), index < 12 ? IdeaStatus.Target : IdeaStatus.Stop, index < 12 ? 2m : -1m);
        }

        BucketStatistics statistics = sums.Statistics();

        statistics.ExpectancyLow!.Value.ShouldBe(-2.7298m, Precision);
        statistics.ExpectancyHigh!.Value.ShouldBe(3.1298m, Precision);
        statistics.ProbabilityLow!.Value.ShouldBe(0.0358m, Precision);
        statistics.ProbabilityHigh!.Value.ShouldBe(0.9229m, Precision);
    }

    // A share of 0 has no spread to widen: the interval of the share is that of independent cases.
    [Test]
    public void Cases_without_a_target_have_the_independent_interval_of_the_share()
    {
        var sums = new ClusteredSums(OneHour);

        for (int index = 0; index < 30; index++)
        {
            sums.Add(Hour.AddHours(index / 3), IdeaStatus.Expired, index % 2 == 0 ? 0.1m : -0.3m);
        }

        BucketStatistics statistics = sums.Statistics();

        statistics.Probability.ShouldBe(0m);
        statistics.ProbabilityLow!.Value.ShouldBe(BucketStatisticsCalculator.Compute(0, 0, 30, -3m, 1.5m).ProbabilityLow!.Value, Precision);
        statistics.ProbabilityHigh!.Value.ShouldBe(BucketStatisticsCalculator.Compute(0, 0, 30, -3m, 1.5m).ProbabilityHigh!.Value, Precision);
    }

    // Scenario "Мало случаев": the counts and the mean, no share, interval or badge under 30 cases.
    [Test]
    public void Fewer_than_30_cases_have_their_counts_and_mean_only()
    {
        var sums = new ClusteredSums(OneHour);

        for (int index = 0; index < 12; index++)
        {
            sums.Add(Hour.AddHours(index), index < 6 ? IdeaStatus.Target : IdeaStatus.Stop, index < 6 ? 2m : -1m);
        }

        sums.Statistics().ShouldBe(new BucketStatistics(12, 6, 6, 0, null, null, null, 0.5m, null, null));
    }

    private static (int, int, int, int, decimal, decimal, int, long, decimal, decimal, long, long) Totals(ClusteredSums sums)
    {
        return (sums.Cases, sums.Targets, sums.Stops, sums.Expired, sums.TotalR, sums.TotalSquaredR, sums.Groups, sums.SquaredCounts, sums.CountsByResults,
            sums.SquaredResults, sums.CountsByTargets, sums.SquaredTargets);
    }
}