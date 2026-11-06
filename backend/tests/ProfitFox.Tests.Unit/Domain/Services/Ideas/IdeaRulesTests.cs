using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Requirements "Правила типов идей по рынку", "Правило «Отката»", "Буфер стопа" and "Идеи сетапа". The pair stands at 105 after a window from
///     100 to 110, ATR 1 and k 0.5, so the buffer is 0.5; a support at 101–102 and a resistance at 115–116.
/// </summary>
public class IdeaRulesTests
{
    private static readonly Zone Support = new Zone(101m, 102m, 3);
    private static readonly Zone Resistance = new Zone(115m, 116m, 2);
    private static readonly IdeaContext Context = new IdeaContext(MarketCategory.Linear, 105m, 100m, 110m, 1m, [Support, Resistance], 0.01m, 0.5m, 0.01m);

    [Test]
    public void Levels_long_stops_below_the_nearest_support_and_targets_the_nearest_resistance()
    {
        IdeaRules.Plan(IdeaType.Levels, IdeaSide.Long, Context, null).ShouldBe(new IdeaPlan(IdeaType.Levels, IdeaSide.Long, 105m, 100.5m, 115m));
    }

    [Test]
    public void Levels_short_stops_above_the_nearest_resistance_and_targets_the_nearest_support()
    {
        IdeaRules.Plan(IdeaType.Levels, IdeaSide.Short, Context with { Price = 112m }, null).ShouldBe(new IdeaPlan(IdeaType.Levels, IdeaSide.Short, 112m, 116.5m, 102m));
    }

    [Test]
    public void Two_r_stops_beyond_the_nearest_zone_and_targets_twice_the_risk()
    {
        IdeaRules.Plan(IdeaType.TwoR, IdeaSide.Long, Context, 2m).ShouldBe(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 105m, 100.5m, 114m));
        IdeaRules.Plan(IdeaType.TwoR, IdeaSide.Short, Context, 2m).ShouldBe(new IdeaPlan(IdeaType.TwoR, IdeaSide.Short, 105m, 116.5m, 82m));
    }

    [Test]
    public void Beyond_extreme_stops_beyond_the_extreme_of_the_setup_window()
    {
        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Long, Context, 2m).ShouldBe(new IdeaPlan(IdeaType.BeyondExtreme, IdeaSide.Long, 105m, 99.5m, 116m));
        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Short, Context, 2m).ShouldBe(new IdeaPlan(IdeaType.BeyondExtreme, IdeaSide.Short, 105m, 110.5m, 94m));
    }

    // Scenario "Откат после импульса".
    [Test]
    public void Pullback_enters_at_the_middle_of_the_move_of_the_window()
    {
        IdeaRules.Plan(IdeaType.Pullback, IdeaSide.Long, Context with { Price = 109m }, 2m).ShouldBe(new IdeaPlan(IdeaType.Pullback, IdeaSide.Long, 105m, 99.5m, 116m));
        IdeaRules.Plan(IdeaType.Pullback, IdeaSide.Short, Context with { Price = 101m }, 2m).ShouldBe(new IdeaPlan(IdeaType.Pullback, IdeaSide.Short, 105m, 110.5m, 94m));
    }

    // Decision 10: a limit the price has already passed would fill at once at a price the market never gave; such a pullback is not made.
    [TestCase(IdeaSide.Long, 104)]
    [TestCase(IdeaSide.Long, 105)]
    [TestCase(IdeaSide.Short, 106)]
    public void Pullback_whose_limit_the_price_has_passed_is_not_made(IdeaSide side, decimal price)
    {
        IdeaRules.Plan(IdeaType.Pullback, side, Context with { Price = price }, 2m).ShouldBeNull();
    }

    // Scenario "Закрытие внутри выбитой зоны": the false breakout of 50.0–50.4 closed at 50.2, inside it; the buffer is 0.3.
    [Test]
    public void Two_r_of_a_setup_zone_stops_beyond_it_though_the_zone_is_not_wholly_below_the_price()
    {
        IdeaContext context = Context with { Price = 50.2m, Zones = [], StopBuffer = 0.3m, SetupZone = new Zone(50.0m, 50.4m, 2) };

        IdeaRules.Plan(IdeaType.TwoR, IdeaSide.Long, context, 2m).ShouldBe(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 50.2m, 49.7m, 51.2m));
    }

    [Test]
    public void Two_r_short_of_a_setup_zone_stops_above_its_high()
    {
        IdeaContext context = Context with { Price = 99.0m, Zones = [], StopBuffer = 0.3m, SetupZone = new Zone(99.4m, 100.0m, 2) };

        IdeaRules.Plan(IdeaType.TwoR, IdeaSide.Short, context, 2m).ShouldBe(new IdeaPlan(IdeaType.TwoR, IdeaSide.Short, 99.0m, 100.3m, 96.4m));
    }

    // The target of «Уровни» is still the nearest zone ahead of the price.
    [Test]
    public void Levels_of_a_setup_zone_stop_beyond_it_and_target_the_nearest_zone_ahead()
    {
        IdeaContext context = Context with { Price = 50.2m, Zones = [new Zone(49.0m, 49.2m, 2), new Zone(52.0m, 52.2m, 2)], StopBuffer = 0.3m, SetupZone = new Zone(50.0m, 50.4m, 2) };

        IdeaRules.Plan(IdeaType.Levels, IdeaSide.Long, context, null).ShouldBe(new IdeaPlan(IdeaType.Levels, IdeaSide.Long, 50.2m, 49.7m, 52.0m));
    }

    // Scenario "Стоп-вход после сжатия": the range 100–102, the price step 0.01, the buffer 0.5 and the price 101.
    [Test]
    public void Stop_entry_enters_a_step_beyond_the_range_and_stops_beyond_its_other_edge()
    {
        IdeaContext context = Context with { Price = 101m, WindowLow = 100m, WindowHigh = 102m };

        IdeaRules.Plan(IdeaType.StopEntry, IdeaSide.Long, context, 2m).ShouldBe(new IdeaPlan(IdeaType.StopEntry, IdeaSide.Long, 102.01m, 99.5m, 107.03m));
        IdeaRules.Plan(IdeaType.StopEntry, IdeaSide.Short, context, 2m).ShouldBe(new IdeaPlan(IdeaType.StopEntry, IdeaSide.Short, 99.99m, 102.5m, 94.97m));
    }

    // The replay takes today's price step while the range of the past lies on an older grid: a range of 99.9–102.1 at a step of 0.25 enters
    // long at 102.35 up to 102.50 and short at 99.65 down to 99.50, away from the market, as the stop and the target go.
    [Test]
    public void Stop_entry_of_a_range_off_the_price_step_enters_on_the_step_away_from_the_market()
    {
        IdeaContext context = Context with { Price = 101m, WindowLow = 99.9m, WindowHigh = 102.1m, Tick = 0.25m };

        IdeaRules.Plan(IdeaType.StopEntry, IdeaSide.Long, context, 2m).ShouldBe(new IdeaPlan(IdeaType.StopEntry, IdeaSide.Long, 102.50m, 99.25m, 109.00m));
        IdeaRules.Plan(IdeaType.StopEntry, IdeaSide.Short, context, 2m).ShouldBe(new IdeaPlan(IdeaType.StopEntry, IdeaSide.Short, 99.50m, 102.75m, 93.00m));
    }

    // A stop order the price has already passed would fill at once, at a price the market never gave.
    [TestCase(IdeaSide.Long, 102.01)]
    [TestCase(IdeaSide.Long, 102.5)]
    [TestCase(IdeaSide.Short, 99.99)]
    public void Stop_entry_whose_entry_the_price_has_passed_is_not_made(IdeaSide side, decimal price)
    {
        IdeaRules.Plan(IdeaType.StopEntry, side, Context with { Price = price, WindowLow = 100m, WindowHigh = 102m }, 2m).ShouldBeNull();
    }

    [Test]
    public void Stop_entry_short_on_spot_is_not_made()
    {
        IdeaRules.Plan(IdeaType.StopEntry, IdeaSide.Short, Context with { Category = MarketCategory.Spot, Price = 101m, WindowLow = 100m, WindowHigh = 102m }, 2m).ShouldBeNull();
    }

    // Scenario "Нет сопротивления".
    [Test]
    public void Without_the_zone_it_needs_an_idea_is_not_made()
    {
        IdeaContext context = Context with { Zones = [Support] };

        IdeaRules.Plan(IdeaType.Levels, IdeaSide.Long, context, null).ShouldBeNull();
        IdeaRules.Plan(IdeaType.TwoR, IdeaSide.Long, context, 2m).ShouldNotBeNull();
        IdeaRules.Plan(IdeaType.TwoR, IdeaSide.Short, context, 2m).ShouldBeNull();
    }

    // Scenario "Цель 3 R": a target of 3 R of the rule of «За экстремумом», entry 100 and stop 98.
    [Test]
    public void Target_in_r_comes_from_the_rule()
    {
        IdeaContext context = Context with { Price = 100m, WindowLow = 98.5m };

        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Long, context, 3m).ShouldBe(new IdeaPlan(IdeaType.BeyondExtreme, IdeaSide.Long, 100m, 98m, 106m));
    }

    // Scenario "Цель 1 R": the reward to the risk of a type with a target in R is the target of its rule, under 1.5 too.
    [Test]
    public void Target_of_one_r_is_made_though_under_one_and_a_half()
    {
        IdeaPlan plan = IdeaRules.Plan(IdeaType.TwoR, IdeaSide.Long, Context, 1m)!;

        (plan.Target, plan.RiskReward).ShouldBe((109.5m, 1m));
    }

    [Test]
    public void An_idea_with_risk_reward_below_one_and_a_half_is_not_made()
    {
        IdeaContext context = Context with { Zones = [Support, new Zone(110m, 111m, 2)] };

        IdeaRules.Plan(IdeaType.Levels, IdeaSide.Long, context, null).ShouldBeNull();
    }

    // Fix: after a fall from 110 to 30 the short stop 110.5 is 80.5 above the entry, so a target of 2R would lie at −131.
    [Test]
    public void Short_whose_target_is_not_above_zero_is_not_made()
    {
        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Short, Context with { Price = 30m }, 2m).ShouldBeNull();
    }

    // Fix: a window low of 0.2 under a buffer of 0.5 would put the long stop at −0.3.
    [Test]
    public void Long_whose_stop_is_not_above_zero_is_not_made()
    {
        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Long, Context with { WindowLow = 0.2m }, 2m).ShouldBeNull();
    }

    // Scenario "Узкий ATR": k × ATR = 0.15 is less than three spreads of 0.1.
    [Test]
    public void Stop_buffer_is_at_least_three_spreads()
    {
        IdeaContext context = Context with { Atr = 0.1m, StopBuffer = 1.5m, Spread = 0.1m };

        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Long, context, 2m)!.Stop.ShouldBe(99.7m);
    }

    // A replay of history has no book: the buffer is k × ATR alone.
    [Test]
    public void Without_a_spread_the_buffer_is_k_times_atr()
    {
        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Long, Context with { Spread = null }, 2m)!.Stop.ShouldBe(99.5m);
    }

    // Fix after review: levels go on the price step of the pair. A stop goes away from the entry and a target towards it, so neither is
    // better than the rules give; the limit of a pullback goes towards the market, so it fills no better than the rules give. Price step
    // 0.25: the long stop 99.7 goes down to 99.5, the target 116.3 down to 116.25; short mirrors it.
    [Test]
    public void Levels_go_on_the_price_step_of_the_pair()
    {
        IdeaContext context = Context with { Price = 105.1m, Atr = 0.3m, StopBuffer = 1m, Tick = 0.25m };

        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Long, context, 2m).ShouldBe(new IdeaPlan(IdeaType.BeyondExtreme, IdeaSide.Long, 105.1m, 99.5m, 116.25m));
        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Short, context, 2m).ShouldBe(new IdeaPlan(IdeaType.BeyondExtreme, IdeaSide.Short, 105.1m, 110.5m, 94.5m));
        IdeaRules.Plan(IdeaType.Levels, IdeaSide.Long, context with { Zones = [Support, new Zone(115.1m, 116m, 2)] }, null)!.Target.ShouldBe(115m);
    }

    // The window from 100 to 110.1 has its middle at 105.05: a buy limit goes up to 105.25, a sell limit down to 105.
    [Test]
    public void Pullback_limit_goes_on_the_price_step_towards_the_market()
    {
        IdeaContext context = Context with { WindowHigh = 110.1m, Tick = 0.25m };

        IdeaRules.Plan(IdeaType.Pullback, IdeaSide.Long, context with { Price = 109m }, 2m)!.Entry.ShouldBe(105.25m);
        IdeaRules.Plan(IdeaType.Pullback, IdeaSide.Short, context with { Price = 101m }, 2m)!.Entry.ShouldBe(105m);
    }

    // Scenario "Падение на spot".
    [Test]
    public void Spot_has_no_short_ideas()
    {
        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Short, Context with { Category = MarketCategory.Spot }, 2m).ShouldBeNull();
        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Long, Context with { Category = MarketCategory.Spot }, 2m).ShouldNotBeNull();
    }
}