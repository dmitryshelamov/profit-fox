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
        IdeaFeatures.Of(95m / 100m - 1m, null, null).DayDrop.ShouldBeTrue();
    }

    [Test]
    public void A_drop_of_the_pair_under_the_bound_is_no_day_drop()
    {
        // Scenario "Падение пары меньше границы": close 95.01 against 100.
        IdeaFeatures.Of(95.01m / 100m - 1m, null, null).DayDrop.ShouldBeFalse();
    }

    [Test]
    public void A_drop_of_btc_from_one_percent_is_a_btc_drop()
    {
        // Scenario "BTC у spot-идеи": 98 700 against 100 000 four hours before.
        IdeaFeatures.Of(null, 98_700m / 100_000m - 1m, null).ShouldBe(new IdeaFeatures(false, true, false));
    }

    [Test]
    public void No_minute_of_btc_is_no_btc_drop()
    {
        // Scenario "Нет минуты BTC".
        IdeaFeatures.Of(null, null, null).BtcDrop.ShouldBeFalse();
    }

    [TestCase(0.0005, true)]
    [TestCase(0.00049, false)]
    public void A_funding_rate_from_the_bound_is_a_high_funding(decimal funding, bool high)
    {
        // Scenario "Funding": the last rate 0.0005.
        IdeaFeatures.Of(null, null, funding).HighFunding.ShouldBe(high);
    }

    [Test]
    public void No_funding_rate_is_no_high_funding()
    {
        // Scenario "Funding у spot": a spot pair has no rate.
        IdeaFeatures.Of(-0.2m, -0.2m, null).ShouldBe(new IdeaFeatures(true, true, false));
    }
}