using ProfitFox.Application.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.IdeaTrades;

public class IdeaTradeCodesTests
{
    [TestCase(IdeaTradeRefusal.BudgetTaken, "budget_taken")]
    [TestCase(IdeaTradeRefusal.AutosBudgetTaken, "autos_budget_taken")]
    [TestCase(IdeaTradeRefusal.MinuteLimit, "minute_limit")]
    [TestCase(IdeaTradeRefusal.SideLimit, "side_limit")]
    public void Refusals_of_the_budgets_and_limits_have_codes_both_ways(IdeaTradeRefusal refusal, string code)
    {
        refusal.ToCode().ShouldBe(code);
        IdeaTradeCodes.TryParseRefusal(code, out IdeaTradeRefusal parsed).ShouldBeTrue();
        parsed.ShouldBe(refusal);
    }
}