using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.Ideas;

/// <summary>Requirements "Правило выхода" and "Допустимые значения правила".</summary>
public class IdeaExitRulesTests
{
    private static readonly DateTimeOffset Applied = DateTimeOffset.Parse("2024-03-01T10:00:00Z");

    // Scenario "Правило по умолчанию".
    [Test]
    public void Rule_never_applied_is_the_default()
    {
        var rules = new IdeaExitRules([]);

        rules.Of(IdeaType.Pullback, Interval.FifteenMinutes).ShouldBe(new IdeaExitRule(IdeaType.Pullback, Interval.FifteenMinutes, 24, 2m, 4, null));
        rules.Of(IdeaType.StopEntry, Interval.OneHour).ShouldBe(new IdeaExitRule(IdeaType.StopEntry, Interval.OneHour, 24, 2m, 12, null));
        rules.Of(IdeaType.TwoR, Interval.FiveMinutes).ShouldBe(new IdeaExitRule(IdeaType.TwoR, Interval.FiveMinutes, 24, 2m, null, null));
        rules.Of(IdeaType.BeyondExtreme, Interval.FiveMinutes).TargetR.ShouldBe(2m);
    }

    // Scenario "Цель «Уровней»".
    [Test]
    public void Levels_have_only_a_lifetime()
    {
        new IdeaExitRules([]).Of(IdeaType.Levels, Interval.OneHour).ShouldBe(new IdeaExitRule(IdeaType.Levels, Interval.OneHour, 24, null, null, null));
    }

    [Test]
    public void Applied_rule_replaces_the_default_of_its_type_and_interval()
    {
        var applied = new IdeaExitRule(IdeaType.Pullback, Interval.FifteenMinutes, 48, 2.5m, 8, Applied);
        var rules = new IdeaExitRules([applied]);

        rules.Of(IdeaType.Pullback, Interval.FifteenMinutes).ShouldBe(applied);
        rules.Of(IdeaType.Pullback, Interval.OneHour).LifeCandles.ShouldBe(24);
    }

    [Test]
    public void Rule_on_the_grid_is_valid()
    {
        IdeaExitRule.Invalid(IdeaType.Pullback, Interval.FifteenMinutes, 48, 2.5m, 8).ShouldBeEmpty();
        IdeaExitRule.Invalid(IdeaType.Levels, Interval.FourHours, 42, null, null).ShouldBeEmpty();
        IdeaExitRule.Invalid(IdeaType.TwoR, Interval.FifteenMinutes, 672, 1m, null).ShouldBeEmpty();
        IdeaExitRule.Invalid(IdeaType.TwoR, Interval.FifteenMinutes, 1, 5m, null).ShouldBeEmpty();
    }

    // Scenario "Цель вне шага", and a target outside 1…5 R.
    [Test]
    public void Target_off_the_step_or_the_range_is_refused()
    {
        IdeaExitRule.Invalid(IdeaType.TwoR, Interval.FifteenMinutes, 24, 2.3m, null).ShouldBe(["target"]);
        IdeaExitRule.Invalid(IdeaType.TwoR, Interval.FifteenMinutes, 24, 0.5m, null).ShouldBe(["target"]);
        IdeaExitRule.Invalid(IdeaType.TwoR, Interval.FifteenMinutes, 24, 5.5m, null).ShouldBe(["target"]);
    }

    [Test]
    public void Lifetime_and_window_off_the_grid_are_refused()
    {
        IdeaExitRule.Invalid(IdeaType.Pullback, Interval.FifteenMinutes, 40, 2m, 5).ShouldBe(["life", "window"]);
        IdeaExitRule.Invalid(IdeaType.TwoR, Interval.FourHours, 48, 2m, null).ShouldBe(["life"]);
    }

    // A value the type has no use for, or one it needs and lacks.
    [Test]
    public void Target_and_window_follow_the_type()
    {
        IdeaExitRule.Invalid(IdeaType.TwoR, Interval.FifteenMinutes, 24, 2m, 4).ShouldBe(["window"]);
        IdeaExitRule.Invalid(IdeaType.Levels, Interval.FifteenMinutes, 24, 2m, null).ShouldBe(["target"]);
        IdeaExitRule.Invalid(IdeaType.TwoR, Interval.FifteenMinutes, 24, null, null).ShouldBe(["target"]);
        IdeaExitRule.Invalid(IdeaType.StopEntry, Interval.FifteenMinutes, 24, 2m, null).ShouldBe(["window"]);
    }
}