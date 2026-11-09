using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Requirement "Издержки идеи" (decision 2 of add-own-idea-numbers): the share of the amount the fees and the slippage take, times the entry,
///     over the distance from the entry to the stop.
/// </summary>
public class IdeaCostsTests
{
    private static readonly FeeRates Linear = new FeeRates(0.02m, 0.055m, 0.05m);
    private static readonly FeeRates Spot = new FeeRates(0.1m, 0.1m, 0.1m);

    // Scenario "Вход по рынку на linear": 0.0021 × 100 / 2.
    [Test]
    public void Market_entry_pays_the_taker_fees_and_the_slippage_of_both_fills()
    {
        IdeaCosts.R(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m), Linear).ShouldBe(0.105m);
    }

    // Scenario "Откат платит меньше": 0.00125 × 100 / 2.
    [Test]
    public void Pullback_pays_the_maker_fee_and_no_slippage_on_its_entry()
    {
        IdeaCosts.R(new IdeaPlan(IdeaType.Pullback, IdeaSide.Long, 100m, 98m, 104m), Linear).ShouldBe(0.0625m);
    }

    // Scenario "Узкий стоп": 0.0021 × 100 / 0.3.
    [Test]
    public void Narrow_stop_costs_more_in_r()
    {
        IdeaCosts.R(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 99.7m, 100.6m), Linear).ShouldBe(0.7m);
    }

    // Scenario "Spot": 0.004 × 100 / 2.
    [Test]
    public void Spot_takes_the_fees_of_its_category()
    {
        IdeaCosts.R(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m), Spot).ShouldBe(0.2m);
    }
}