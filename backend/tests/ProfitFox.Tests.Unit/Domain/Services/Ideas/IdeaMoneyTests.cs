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

    [Test]
    public void Spot_takes_its_own_fees()
    {
        IdeaShares shares = IdeaMoney.PerUsdt(new IdeaPlan(IdeaType.Levels, IdeaSide.Long, 100m, 98m, 104m), Spot);

        shares.ShouldBe(new IdeaShares(0.03796m, 0.02198m, 0.02m));
    }
}