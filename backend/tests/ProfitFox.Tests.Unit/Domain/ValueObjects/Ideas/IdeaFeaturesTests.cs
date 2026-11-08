using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.Ideas;

/// <summary>
///     Requirements "Падение пары за сутки", "Падение BTC за 4 часа" and "Высокий funding": a drop of the pair by 5 % over a day, of BTC by 1 % over
///     4 hours and a funding rate from 0.05 %, known on the minute a signal fired; a number that is not known gives no feature.
/// </summary>
public class IdeaFeaturesTests
{
    [Test]
    public void A_drop_of_the_pair_right_at_the_bound_is_a_day_drop()
    {
        // Scenario "Падение пары ровно на границе": close 95 against 100 a day before.
        IdeaFeatures.Of(95m / 100m - 1m, null, null, null, Interval.OneHour).DayDrop.ShouldBeTrue();
    }

    [Test]
    public void A_drop_of_the_pair_under_the_bound_is_no_day_drop()
    {
        // Scenario "Падение пары меньше границы": close 95.01 against 100.
        IdeaFeatures.Of(95.01m / 100m - 1m, null, null, null, Interval.OneHour).DayDrop.ShouldBeFalse();
    }

    [Test]
    public void A_drop_of_btc_from_one_percent_is_a_btc_drop()
    {
        // Scenario "BTC у spot-идеи": 98 700 against 100 000 four hours before.
        IdeaFeatures.Of(null, 98_700m / 100_000m - 1m, null, null, Interval.OneHour).ShouldBe(new IdeaFeatures(false, true, false, false));
    }

    [Test]
    public void No_minute_of_btc_is_no_btc_drop()
    {
        // Scenario "Нет минуты BTC".
        IdeaFeatures.Of(null, null, null, null, Interval.OneHour).BtcDrop.ShouldBeFalse();
    }

    [TestCase(0.0005, true)]
    [TestCase(0.00049, false)]
    public void A_funding_rate_from_the_bound_is_a_high_funding(decimal funding, bool high)
    {
        // Scenario "Funding": the last rate 0.0005.
        IdeaFeatures.Of(null, null, funding, null, Interval.OneHour).HighFunding.ShouldBe(high);
    }

    [Test]
    public void No_funding_rate_is_no_high_funding()
    {
        // Scenario "Funding у spot": a spot pair has no rate.
        IdeaFeatures.Of(-0.2m, -0.2m, null, null, Interval.OneHour).ShouldBe(new IdeaFeatures(true, true, false, false));
    }

    // Decision 1: the enum names a feature of the level in the breakdown, the conditions of the autos and the codes of the API.
    [Test]
    public void Level_tells_whether_it_has_a_feature_by_its_name()
    {
        var level = new IdeaFeatures(true, false, true, true);

        (level.Has(IdeaFeature.DayDrop), level.Has(IdeaFeature.BtcDrop), level.Has(IdeaFeature.HighFunding), level.Has(IdeaFeature.Burst))
            .ShouldBe((true, false, true, true));
        new IdeaFeatures(true, true, true, false).Has(IdeaFeature.Burst).ShouldBeFalse();
    }

    // Requirement "Признак всплеска" (decision 1 of add-burst-and-auto-limits): distinct pairs of the side on the interval in the minute, from
    // the threshold of the interval.
    [TestCase(Interval.FiveMinutes, 60)]
    [TestCase(Interval.FifteenMinutes, 25)]
    [TestCase(Interval.OneHour, 8)]
    [TestCase(Interval.FourHours, 4)]
    public void The_threshold_of_a_burst_is_that_of_its_interval(Interval interval, int threshold)
    {
        IdeaFeatures.BurstThreshold(interval).ShouldBe(threshold);
        IdeaFeatures.Of(null, null, null, threshold, interval).Burst.ShouldBeTrue();
        IdeaFeatures.Of(null, null, null, threshold - 1, interval).Burst.ShouldBeFalse();
    }

    [Test]
    public void A_burst_on_the_hour_of_nine_pairs()
    {
        // Scenario "Всплеск на часе": longs on 1 h by 9 distinct pairs.
        IdeaFeatures.Of(null, null, null, 9, Interval.OneHour).ShouldBe(new IdeaFeatures(false, false, false, true));
    }

    [Test]
    public void No_count_of_pairs_is_no_burst()
    {
        // A live idea before the burst was kept: its count is none.
        IdeaFeatures.Of(null, null, null, null, Interval.FourHours).Burst.ShouldBeFalse();
    }
}