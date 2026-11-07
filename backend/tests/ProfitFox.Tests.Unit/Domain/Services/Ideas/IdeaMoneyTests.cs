using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>Requirements "Чтение идей" and "Фильтры списка идей": the money of an idea on 1 USDT of amount, after the fees of <see cref="CaseResult" />.</summary>
public class IdeaMoneyTests
{
    private static readonly FeeRates Linear = new FeeRates(0.02m, 0.055m);
    private static readonly FeeRates Spot = new FeeRates(0.1m, 0.1m);
    private static readonly FeeRates Slipping = new FeeRates(0.02m, 0.055m, 0.05m);

    // Scenario "Деньги на 1 USDT суммы": (4 − 0.055 − 0.0572) / 100, (2 + 0.055 + 0.0539) / 100, 2 / 100.
    [Test]
    public void Market_long_pays_the_taker_fee_on_the_entry_and_on_the_exit()
    {
        IdeaShares shares = IdeaMoney.PerUsdt(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m), Linear);

        shares.ShouldBe(new IdeaShares(0.038878m, 0.021089m, 0.02m));
    }

    // The distances are absolute and the exit fee follows the price it exits at: (4 − 0.055 − 0.0528) / 100, (2 + 0.055 + 0.0561) / 100.
    [Test]
    public void Short_counts_the_distances_the_same_way()
    {
        IdeaShares shares = IdeaMoney.PerUsdt(new IdeaPlan(IdeaType.TwoR, IdeaSide.Short, 100m, 102m, 96m), Linear);

        shares.ShouldBe(new IdeaShares(0.038922m, 0.021111m, 0.02m));
    }

    // A pullback enters with a limit: maker on the entry, taker on the exit.
    [Test]
    public void Pullback_pays_the_maker_fee_on_its_entry()
    {
        IdeaShares shares = IdeaMoney.PerUsdt(new IdeaPlan(IdeaType.Pullback, IdeaSide.Long, 100m, 98m, 104m), Linear);

        shares.ShouldBe(new IdeaShares(0.039228m, 0.020739m, 0.02m));
    }

    // A stop entry fills a stop order: taker on the entry, as at the market.
    [Test]
    public void Stop_entry_pays_the_taker_fee_on_its_entry()
    {
        IdeaShares shares = IdeaMoney.PerUsdt(new IdeaPlan(IdeaType.StopEntry, IdeaSide.Long, 100m, 98m, 104m), Linear);

        shares.ShouldBe(new IdeaShares(0.038878m, 0.021089m, 0.02m));
    }

    [Test]
    public void Spot_takes_its_own_fees()
    {
        IdeaShares shares = IdeaMoney.PerUsdt(new IdeaPlan(IdeaType.Levels, IdeaSide.Long, 100m, 98m, 104m), Spot);

        shares.ShouldBe(new IdeaShares(0.03796m, 0.02198m, 0.02m));
    }

    // Requirement "Проскальзывание идей": the entry buys at 100.05, the target sells at 103.948 and the stop at 97.951, the fees on those
    // prices: (3.898 − 0.0550275 − 0.0571714) / 100, (2.099 + 0.0550275 + 0.0538731) / 100.
    [Test]
    public void Market_long_fills_its_entry_and_its_exits_worse_by_the_slippage()
    {
        IdeaShares shares = IdeaMoney.PerUsdt(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m), Slipping);

        shares.ShouldBe(new IdeaShares(0.037858011m, 0.0220790055m, 0.02m));
    }

    // The short sells its entry at 99.95 and buys back at 96.048 at the target and at 102.051 at the stop:
    // (3.902 − 0.0549725 − 0.0528264) / 100, (2.101 + 0.0549725 + 0.05612805) / 100.
    [Test]
    public void Market_short_sells_lower_and_buys_higher_by_the_slippage()
    {
        IdeaShares shares = IdeaMoney.PerUsdt(new IdeaPlan(IdeaType.TwoR, IdeaSide.Short, 100m, 102m, 96m), Slipping);

        shares.ShouldBe(new IdeaShares(0.037942011m, 0.0221210055m, 0.02m));
    }

    // Scenario "Цель не окупает комиссии": without fees, a target 0.05 above the entry sells at 99.999975 after buying at 100.05, a loss of
    // 0.050025 on 100, so there is no break-even.
    [Test]
    public void Target_closer_than_the_slippage_loses_at_the_target()
    {
        IdeaShares shares = IdeaMoney.PerUsdt(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 100.05m), new FeeRates(0m, 0m, 0.05m));

        shares.ProfitPerUsdt.ShouldBe(-0.00050025m);
        shares.BreakEven.ShouldBeNull();
    }

    // Scenario "Лимитный вход «Отката»": the limit fills at 100, the exits still slip: (3.948 − 0.02 − 0.0571714) / 100, (2.049 + 0.02 + 0.0538731) / 100.
    [Test]
    public void Pullback_enters_at_its_limit_without_the_slippage()
    {
        IdeaShares shares = IdeaMoney.PerUsdt(new IdeaPlan(IdeaType.Pullback, IdeaSide.Long, 100m, 98m, 104m), Slipping);

        shares.ShouldBe(new IdeaShares(0.038708286m, 0.0212287305m, 0.02m));
    }

    // Scenario "R:R идеи после комиссий": the profit on 1 USDT over the loss on 1 USDT, not the prices' 2.
    [Test]
    public void Net_risk_reward_is_the_profit_over_the_loss_on_1_usdt()
    {
        IdeaShares shares = IdeaMoney.PerUsdt(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m), Linear);

        shares.NetRiskReward.ShouldBe(0.038878m / 0.021089m);
    }

    // Scenario "Близкий стоп": a stop 0.64 % from the entry leaves 1.56 of the 2.
    [Test]
    public void A_close_stop_leaves_the_fees_a_larger_part_of_the_reward()
    {
        IdeaShares shares = IdeaMoney.PerUsdt(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 44.06m, 43.78m, 44.62m), Linear);

        decimal.Round(shares.NetRiskReward, 2).ShouldBe(1.56m);
    }
}