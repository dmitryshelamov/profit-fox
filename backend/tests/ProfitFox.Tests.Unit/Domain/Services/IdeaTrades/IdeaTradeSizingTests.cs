using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Services.IdeaTrades;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.IdeaTrades;

/// <summary>Requirement "Расчёт входа": the volume by the amount, rounded down to the step, and what that volume costs, risks and earns.</summary>
public class IdeaTradeSizingTests
{
    private static readonly FeeRates Linear = new FeeRates(0.02m, 0.055m);
    private static readonly InstrumentRules Rules = new InstrumentRules(0.01m, 0.01m, 0.01m, 5m);

    // Scenario "Объём по сумме": 47.3 / 100 = 0.473 rounds down to 0.47; on 1 USDT the loss is 0.021089 and the profit 0.038878.
    [Test]
    public void Volume_by_the_amount_rounds_down_to_the_step()
    {
        IdeaTradeSize size = IdeaTradeSizing.Calculate(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m), Linear, 47.3m, Rules);

        size.Quantity.ShouldBe(0.47m);
        size.Amount.ShouldBe(47m);
        size.Risk.ShouldBe(0.991183m);
        size.Profit.ShouldBe(1.827266m);
        size.RiskReward.ShouldBe(0.038878m / 0.021089m);
        Math.Round(size.RiskReward, 2).ShouldBe(1.84m);
    }

    // Scenario "Сумма меньше шага": 0.5 USDT buys less than the step of 0.01 at the price of 100.
    [Test]
    public void Amount_below_one_step_is_zero()
    {
        IdeaTradeSize size = IdeaTradeSizing.Calculate(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m), Linear, 0.5m, Rules);

        size.Quantity.ShouldBe(0m);
        size.Amount.ShouldBe(0m);
        size.Risk.ShouldBe(0m);
        size.Profit.ShouldBe(0m);
    }
}