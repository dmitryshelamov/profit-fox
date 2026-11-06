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
        Match(Idea(), Bucket(of), Everything with { Badge = chosen }).ShouldBe(fits);
    }

    [Test]
    public void Idea_without_a_bucket_does_not_fit_a_chosen_badge()
    {
        Match(Idea(), null, Everything with { Badge = IdeaBadge.Down }).ShouldBeFalse();
    }

    // Scenario "Любой значок": the bucket says «мало данных» and the auto takes any badge.
    [Test]
    public void Any_badge_fits_the_idea_without_a_badge()
    {
        Match(Idea(), Bucket(null), Everything).ShouldBeTrue();
        Match(Idea(), null, Everything).ShouldBeTrue();
    }

    private static bool Match(Idea idea, BucketStatistics? bucket, IdeaAutoConditions conditions)
    {
        return AutoConditions.Match(idea, bucket, conditions);
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