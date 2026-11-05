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
    private static readonly IdeaContext Context = new IdeaContext(MarketCategory.Linear, 105m, 100m, 110m, 1m, [Support, Resistance], 0.01m, 0.5m);

    [Test]
    public void Levels_long_stops_below_the_nearest_support_and_targets_the_nearest_resistance()
    {
        IdeaRules.Plan(IdeaType.Levels, IdeaSide.Long, Context).ShouldBe(new IdeaPlan(IdeaType.Levels, IdeaSide.Long, 105m, 100.5m, 115m));
    }

    [Test]
    public void Levels_short_stops_above_the_nearest_resistance_and_targets_the_nearest_support()
    {
        IdeaRules.Plan(IdeaType.Levels, IdeaSide.Short, Context with { Price = 112m }).ShouldBe(new IdeaPlan(IdeaType.Levels, IdeaSide.Short, 112m, 116.5m, 102m));
    }

    [Test]
    public void Two_r_stops_beyond_the_nearest_zone_and_targets_twice_the_risk()
    {
        IdeaRules.Plan(IdeaType.TwoR, IdeaSide.Long, Context).ShouldBe(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 105m, 100.5m, 114m));
        IdeaRules.Plan(IdeaType.TwoR, IdeaSide.Short, Context).ShouldBe(new IdeaPlan(IdeaType.TwoR, IdeaSide.Short, 105m, 116.5m, 82m));
    }

    [Test]
    public void Beyond_extreme_stops_beyond_the_extreme_of_the_setup_window()
    {
        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Long, Context).ShouldBe(new IdeaPlan(IdeaType.BeyondExtreme, IdeaSide.Long, 105m, 99.5m, 116m));
        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Short, Context).ShouldBe(new IdeaPlan(IdeaType.BeyondExtreme, IdeaSide.Short, 105m, 110.5m, 94m));
    }

    // Scenario "Откат после импульса".
    [Test]
    public void Pullback_enters_at_the_middle_of_the_move_of_the_window()
    {
        IdeaRules.Plan(IdeaType.Pullback, IdeaSide.Long, Context with { Price = 109m }).ShouldBe(new IdeaPlan(IdeaType.Pullback, IdeaSide.Long, 105m, 99.5m, 116m));
        IdeaRules.Plan(IdeaType.Pullback, IdeaSide.Short, Context with { Price = 101m }).ShouldBe(new IdeaPlan(IdeaType.Pullback, IdeaSide.Short, 105m, 110.5m, 94m));
    }

    // Decision 10: a limit the price has already passed would fill at once at a price the market never gave; such a pullback is not made.
    [TestCase(IdeaSide.Long, 104)]
    [TestCase(IdeaSide.Long, 105)]
    [TestCase(IdeaSide.Short, 106)]
    public void Pullback_whose_limit_the_price_has_passed_is_not_made(IdeaSide side, decimal price)
    {
        IdeaRules.Plan(IdeaType.Pullback, side, Context with { Price = price }).ShouldBeNull();
    }

    // Scenario "Нет сопротивления".
    [Test]
    public void Without_the_zone_it_needs_an_idea_is_not_made()
    {
        IdeaContext context = Context with { Zones = [Support] };

        IdeaRules.Plan(IdeaType.Levels, IdeaSide.Long, context).ShouldBeNull();
        IdeaRules.Plan(IdeaType.TwoR, IdeaSide.Long, context).ShouldNotBeNull();
        IdeaRules.Plan(IdeaType.TwoR, IdeaSide.Short, context).ShouldBeNull();
    }

    [Test]
    public void An_idea_with_risk_reward_below_one_and_a_half_is_not_made()
    {
        IdeaContext context = Context with { Zones = [Support, new Zone(110m, 111m, 2)] };

        IdeaRules.Plan(IdeaType.Levels, IdeaSide.Long, context).ShouldBeNull();
    }

    // Scenario "Узкий ATR": k × ATR = 0.15 is less than three spreads of 0.1.
    [Test]
    public void Stop_buffer_is_at_least_three_spreads()
    {
        IdeaContext context = Context with { Atr = 0.1m, StopBuffer = 1.5m, Spread = 0.1m };

        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Long, context)!.Stop.ShouldBe(99.7m);
    }

    // A replay of history has no book: the buffer is k × ATR alone.
    [Test]
    public void Without_a_spread_the_buffer_is_k_times_atr()
    {
        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Long, Context with { Spread = null })!.Stop.ShouldBe(99.5m);
    }

    // Scenario "Падение на spot".
    [Test]
    public void Spot_has_no_short_ideas()
    {
        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Short, Context with { Category = MarketCategory.Spot }).ShouldBeNull();
        IdeaRules.Plan(IdeaType.BeyondExtreme, IdeaSide.Long, Context with { Category = MarketCategory.Spot }).ShouldNotBeNull();
    }
}