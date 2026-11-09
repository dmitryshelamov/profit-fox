using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.IdeaAutos;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.IdeaAutos;

/// <summary>Requirements "Условия автомата" and "Значок не хуже выбранного": a table of ideas × conditions with the answer of each condition.</summary>
public class AutoConditionsTests
{
    private static readonly DateTimeOffset Fired = new(2026, 11, 7, 13, 20, 0, TimeSpan.Zero);

    private static readonly IdeaAutoConditions Everything = new IdeaAutoConditions(
        [IdeaSetup.Impulse, IdeaSetup.Breakout],
        [IdeaType.TwoR, IdeaType.Pullback],
        [Interval.FifteenMinutes, Interval.OneHour],
        null,
        null,
        null,
        1.2m,
        0);

    [Test]
    public void Idea_that_meets_every_condition_fits()
    {
        Match(Idea(), null, Everything).ShouldBeTrue();
    }

    // Scenario "Не тот ТФ".
    [Test]
    public void Interval_outside_the_marked_ones_does_not_fit()
    {
        Match(Idea(interval: Interval.FiveMinutes), null, Everything).ShouldBeFalse();
        Match(Idea(interval: Interval.OneHour), null, Everything).ShouldBeTrue();
    }

    [Test]
    public void Setup_outside_the_marked_ones_does_not_fit()
    {
        Match(Idea(setup: IdeaSetup.Squeeze), null, Everything).ShouldBeFalse();
        Match(Idea(setup: IdeaSetup.Breakout), null, Everything).ShouldBeTrue();
    }

    [Test]
    public void Type_outside_the_marked_ones_does_not_fit()
    {
        Match(Idea(type: IdeaType.Levels), null, Everything).ShouldBeFalse();
        Match(Idea(type: IdeaType.Pullback), null, Everything).ShouldBeTrue();
    }

    [Test]
    public void Side_fits_when_it_is_the_chosen_one_or_both_are_chosen()
    {
        Match(Idea(side: IdeaSide.Short), null, Everything with { Side = IdeaSide.Long }).ShouldBeFalse();
        Match(Idea(side: IdeaSide.Long), null, Everything with { Side = IdeaSide.Long }).ShouldBeTrue();
        Match(Idea(side: IdeaSide.Short), null, Everything).ShouldBeTrue();
    }

    [Test]
    public void Market_fits_when_it_is_the_chosen_one_or_both_are_chosen()
    {
        var spot = new Instrument(MarketCategory.Spot, "DOGEUSDT");

        Match(Idea(instrument: spot), null, Everything with { Category = MarketCategory.Linear }).ShouldBeFalse();
        Match(Idea(), null, Everything with { Category = MarketCategory.Linear }).ShouldBeTrue();
        Match(Idea(instrument: spot), null, Everything).ShouldBeTrue();
    }

    // Scenario "Мало случаев": the bucket has 64 cases, the auto wants 100.
    [Test]
    public void Bucket_with_fewer_cases_than_the_minimum_does_not_fit()
    {
        IdeaAutoConditions hundred = Everything with { MinCases = 100 };

        Match(Idea(), Bucket(cases: 64), hundred).ShouldBeFalse();
        Match(Idea(), Bucket(cases: 100), hundred).ShouldBeTrue();
    }

    [Test]
    public void Idea_without_a_bucket_has_no_cases()
    {
        Match(Idea(), null, Everything with { MinCases = 1 }).ShouldBeFalse();
        Match(Idea(), null, Everything with { MinCases = 0 }).ShouldBeTrue();
    }

    // Scenario "Выбран ≈": the ideas with ▲, ≈, ▼ and «мало данных» are born; ▲ and ≈ fit.
    [TestCase(IdeaBadge.Up, IdeaBadge.Up, true)]
    [TestCase(IdeaBadge.Up, IdeaBadge.Flat, false)]
    [TestCase(IdeaBadge.Up, IdeaBadge.Down, false)]
    [TestCase(IdeaBadge.Up, null, false)]
    [TestCase(IdeaBadge.Flat, IdeaBadge.Up, true)]
    [TestCase(IdeaBadge.Flat, IdeaBadge.Flat, true)]
    [TestCase(IdeaBadge.Flat, IdeaBadge.Down, false)]
    [TestCase(IdeaBadge.Flat, null, false)]
    [TestCase(IdeaBadge.Down, IdeaBadge.Up, true)]
    [TestCase(IdeaBadge.Down, IdeaBadge.Flat, true)]
    [TestCase(IdeaBadge.Down, IdeaBadge.Down, true)]
    [TestCase(IdeaBadge.Down, null, false)]
    public void Badge_fits_when_it_is_not_worse_than_the_chosen_one(IdeaBadge chosen, IdeaBadge? of, bool fits)
    {
        AutoConditions.Match(Idea(), Bucket(), of, Everything with { Badge = chosen }).ShouldBe(fits);
    }

    // Scenario "Две идеи одной корзины" (decision 5 of add-own-idea-numbers): the badge is that of the idea, not of its bucket of ≈; the idea of
    // the wide stop with ≈ fits an auto of ≈, the idea of the narrow stop with ▼ does not.
    [Test]
    public void Badge_is_that_of_the_idea_not_of_its_bucket()
    {
        BucketStatistics bucket = Bucket(IdeaBadge.Flat);
        IdeaAutoConditions flat = Everything with { Badge = IdeaBadge.Flat };

        (AutoConditions.Match(Idea(), bucket, IdeaBadge.Flat, flat), AutoConditions.Match(Idea(), bucket, IdeaBadge.Down, flat)).ShouldBe((true, false));
    }

    [Test]
    public void Idea_without_a_bucket_does_not_fit_a_chosen_badge()
    {
        Match(Idea(), null, Everything with { Badge = IdeaBadge.Down }).ShouldBeFalse();
    }

    // Scenario "Любой значок": the bucket says «мало данных», the idea has no badge, and the auto takes any badge.
    [Test]
    public void Any_badge_fits_the_idea_without_a_badge()
    {
        Match(Idea(), Bucket(null), Everything).ShouldBeTrue();
        Match(Idea(), null, Everything).ShouldBeTrue();
    }

    // Scenario "Только после падения пары": an idea without a day drop does not fit, one with it does.
    [Test]
    public void Only_with_a_feature_takes_the_ideas_that_have_it()
    {
        IdeaAutoConditions dropped = Everything with { DayDrop = FeatureCondition.With };

        Match(Idea(), null, dropped).ShouldBeFalse();
        Match(Idea() with { PairChange = -0.07m }, null, dropped).ShouldBeTrue();
    }

    // Scenario "Только без высокого funding": an idea with a high funding does not fit, one without it does.
    [Test]
    public void Only_without_a_feature_takes_the_ideas_that_do_not_have_it()
    {
        IdeaAutoConditions calm = Everything with { HighFunding = FeatureCondition.Without };

        Match(Idea() with { Funding = 0.0006m }, null, calm).ShouldBeFalse();
        Match(Idea() with { Funding = 0.0001m }, null, calm).ShouldBeTrue();
    }

    // The drop of BTC is checked by its own condition.
    [Test]
    public void Drop_of_btc_is_checked_by_its_condition()
    {
        Match(Idea(), null, Everything with { BtcDrop = FeatureCondition.With }).ShouldBeFalse();
        Match(Idea() with { BtcChange = -0.02m }, null, Everything with { BtcDrop = FeatureCondition.Without }).ShouldBeFalse();
    }

    // Scenario "Только без всплеска": longs on 1 h by 9 pairs in the minute are a burst, so an auto «только без» takes none of them, one «только
    // с» takes them, and neither takes an idea of the other.
    [Test]
    public void Burst_is_checked_by_its_condition()
    {
        Idea burst = Idea(Interval.OneHour) with { BurstPairs = 9 };
        Idea calm = Idea(Interval.OneHour) with { BurstPairs = 7 };

        (Match(burst, null, Everything with { Burst = FeatureCondition.Without }), Match(calm, null, Everything with { Burst = FeatureCondition.Without })).ShouldBe((false, true));
        (Match(burst, null, Everything with { Burst = FeatureCondition.With }), Match(calm, null, Everything with { Burst = FeatureCondition.With })).ShouldBe((true, false));
    }

    // Scenario "Признак любой": with all three «любой» the features do not matter.
    [Test]
    public void Any_feature_takes_ideas_with_and_without_it()
    {
        Match(Idea() with { PairChange = -0.07m, BtcChange = -0.02m, Funding = 0.0006m }, null, Everything).ShouldBeTrue();
        Match(Idea(), null, Everything).ShouldBeTrue();
    }

    // An idea with the badge of its bucket.
    private static bool Match(Idea idea, BucketStatistics? bucket, IdeaAutoConditions conditions)
    {
        return AutoConditions.Match(idea, bucket, bucket?.Badge, conditions);
    }

    private static Idea Idea(
        Interval interval = Interval.FifteenMinutes,
        IdeaSetup setup = IdeaSetup.Impulse,
        IdeaType type = IdeaType.TwoR,
        IdeaSide side = IdeaSide.Long,
        Instrument? instrument = null)
    {
        return IdeaGenerator.Generate(Fired, instrument: instrument, setup: setup, type: type, side: side, interval: interval);
    }

    private static BucketStatistics Bucket(IdeaBadge? badge = null, int cases = 400)
    {
        return badge switch
        {
            IdeaBadge.Up => new BucketStatistics(cases, 0, 0, 0, 0.4m, 0.3m, 0.5m, 0.2m, 0.1m, 0.3m),
            IdeaBadge.Flat => new BucketStatistics(cases, 0, 0, 0, 0.4m, 0.3m, 0.5m, 0.0m, -0.1m, 0.1m),
            IdeaBadge.Down => new BucketStatistics(cases, 0, 0, 0, 0.4m, 0.3m, 0.5m, -0.2m, -0.3m, -0.1m),
            _ => new BucketStatistics(cases, 0, 0, 0, null, null, null, 0m, null, null)
        };
    }
}