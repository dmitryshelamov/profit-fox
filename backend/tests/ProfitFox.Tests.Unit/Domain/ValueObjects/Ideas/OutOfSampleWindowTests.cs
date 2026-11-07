using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.Ideas;

/// <summary>
///     Requirements "Подбор и проверка", "Конец окна проверки" and "Очистка подбора" of check-idea-exit-rules-out-of-sample: the bounds of the
///     check, the horizon of a rule, the part and the block of a signal by the time it fired.
/// </summary>
public class OutOfSampleWindowTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-06-01T00:00:00Z");
    private static readonly IdeaExitRules Defaults = new IdeaExitRules([]);
    private static readonly IdeaExitRule Market = Defaults.Of(IdeaType.TwoR, Interval.FifteenMinutes);

    // A market rule enters within a minute; a pullback within its window. The longest rule of the grid lives 7 days.
    [Test]
    public void Horizon_is_the_entry_window_and_the_lifetime()
    {
        OutOfSampleWindow.Horizon(Market).ShouldBe(TimeSpan.FromHours(6) + TimeSpan.FromMinutes(1));
        OutOfSampleWindow.Horizon(Defaults.Of(IdeaType.Pullback, Interval.FifteenMinutes)).ShouldBe(TimeSpan.FromHours(7));
    }

    // Scenario "Конец окна проверки": the window ends the horizon of the longest rule of the grid before now, 7 days and the window.
    [Test]
    public void Window_ends_the_horizon_of_the_longest_rule_of_the_grid_before_now()
    {
        OutOfSampleWindow.Of(Market, Now).ShouldBe(new OutOfSampleWindow(Now.AddDays(-90), Now.AddDays(-30), Now.AddDays(-7).AddMinutes(-1)));
        OutOfSampleWindow.Of(Defaults.Of(IdeaType.Pullback, Interval.FourHours) with { EntryCandles = 8 }, Now).To.ShouldBe(Now.AddDays(-7).AddHours(-32));
    }

    // Scenario "Очистка подбора": a signal of the selection whose horizon reaches the check is in neither part; one at or after the end of the
    // window, or before its start, in none.
    [Test]
    public void Part_of_a_signal_leaves_out_the_horizon_of_the_rule_before_the_check()
    {
        var window = OutOfSampleWindow.Of(Market, Now);
        TimeSpan horizon = OutOfSampleWindow.Horizon(Market);
        DateTimeOffset purged = window.CheckFrom - horizon;

        window.PartOf(horizon, window.From).ShouldBe(OutOfSamplePart.Selection);
        window.PartOf(horizon, purged.AddTicks(-1)).ShouldBe(OutOfSamplePart.Selection);
        window.PartOf(horizon, purged).ShouldBeNull();
        window.PartOf(horizon, window.CheckFrom).ShouldBe(OutOfSamplePart.Check);
        window.PartOf(horizon, window.To.AddTicks(-1)).ShouldBe(OutOfSamplePart.Check);
        window.PartOf(horizon, window.To).ShouldBeNull();
        window.PartOf(horizon, window.From.AddTicks(-1)).ShouldBeNull();
    }

    // Requirement "Разбиения проверки": 6 equal blocks from the start to the end of the window; the tail of a block is its last horizon of
    // the rule.
    [Test]
    public void Block_of_a_signal_is_one_of_6_equal_blocks_and_its_tail_the_last_horizon_of_the_rule()
    {
        var window = new OutOfSampleWindow(Now.AddDays(-90), Now.AddDays(-30), Now.AddDays(-30));
        TimeSpan horizon = OutOfSampleWindow.Horizon(Market);

        window.BlockOf(horizon, window.From).ShouldBe((0, false));
        window.BlockOf(horizon, Now.AddDays(-80).AddTicks(-1) - horizon).ShouldBe((0, false));
        window.BlockOf(horizon, Now.AddDays(-80) - horizon).ShouldBe((0, true));
        window.BlockOf(horizon, Now.AddDays(-80)).ShouldBe((1, false));
        window.BlockOf(horizon, window.To.AddTicks(-1)).ShouldBe((5, true));
        window.BlockOf(horizon, window.To).ShouldBeNull();
        window.BlockOf(horizon, window.From.AddTicks(-1)).ShouldBeNull();
    }
}