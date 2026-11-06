using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.Ideas;

/// <summary>Requirement "Безубыток идеи".</summary>
public class IdeaSharesTests
{
    // Scenario "Безубыток с комиссиями": linear long 100 → 98 / 104, taker 0.055 %; about 35 %, not the 33 % of 2R without fees.
    [Test]
    public void Break_even_is_the_loss_over_the_profit_and_the_loss()
    {
        new IdeaShares(0.038878m, 0.021089m, 0.02m).BreakEven.ShouldBe(0.021089m / (0.038878m + 0.021089m));
    }

    // Scenario "Цель не окупает комиссии".
    [TestCase(-0.0001)]
    [TestCase(0)]
    public void Target_that_does_not_cover_the_fees_has_no_break_even(decimal profit)
    {
        new IdeaShares(profit, 0.0102m, 0.01m).BreakEven.ShouldBeNull();
    }
}