using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Services.IdeaTrades;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.IdeaTrades;

/// <summary>Requirement "Расчёт входа": the volume by the risk, rounded down to the step, and what that volume costs and risks.</summary>
public class IdeaTradeSizingTests
{
    private static readonly FeeRates Linear = new FeeRates(0.02m, 0.055m);
    private static readonly InstrumentRules Rules = new InstrumentRules(0.01m, 0.01m, 0.01m, 5m);

    // Scenario "Объём по риску": the loss on 1 USDT is 0.021089, so 1 / (100 * 0.021089) = 0.474 rounds down to 0.47.
    [Test]
    public void Volume_by_the_risk_rounds_down_to_the_step()
    {
        IdeaTradeSize size = IdeaTradeSizing.Calculate(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m), Linear, 1m, 200m, Rules);

        size.Quantity.ShouldBe(0.47m);
        size.Amount.ShouldBe(47m);
        size.Risk.ShouldBe(0.991183m);
        size.RiskReward.ShouldBe(0.038878m / 0.021089m);
        Math.Round(size.RiskReward, 2).ShouldBe(1.84m);
        size.CappedByAmount.ShouldBeFalse();
    }

    // Scenario "Сумма упёрлась в предел": the stop 0.2 % from the entry would need about 321 USDT for the risk of 1 USDT.
    [Test]
    public void Amount_above_the_limit_is_cut_and_the_risk_falls_below_the_asked()
    {
        IdeaTradeSize size = IdeaTradeSizing.Calculate(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 99.8m, 100.4m), Linear, 1m, 200m, Rules);

        size.Quantity.ShouldBe(2m);
        size.Amount.ShouldBe(200m);
        size.Risk.ShouldBeLessThan(1m);
        size.CappedByAmount.ShouldBeTrue();
    }

    [Test]
    public void Volume_below_one_step_is_zero()
    {
        IdeaTradeSize size = IdeaTradeSizing.Calculate(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m), Linear, 0.01m, 200m, Rules);

        size.Quantity.ShouldBe(0m);
        size.Amount.ShouldBe(0m);
        size.Risk.ShouldBe(0m);
    }
}