using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.IdeaTrades;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.LiveTrading;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.IdeaTrades;

/// <summary>Requirement "Когда вход отклоняется": every reason that stops an entry has a test of its own.</summary>
public class IdeaTradeRefusalsTests
{
    private static readonly IdeaTradingLimits Limits = new IdeaTradingLimits(1.5m, 5, 5m);
    private static readonly IdeaPlan Long = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 23.48m, 23.01m, 24.42m);
    private static readonly InstrumentRules Rules = new InstrumentRules(0.01m, 0.01m, 0.1m, 5m);

    [Test]
    public void Entry_that_breaks_nothing_has_no_reason()
    {
        Refuse().ShouldBeEmpty();
    }

    [TestCase(IdeaStatus.Target)]
    [TestCase(IdeaStatus.Stop)]
    [TestCase(IdeaStatus.Expired)]
    public void Idea_that_is_over_is_named(IdeaStatus status)
    {
        Refuse(status: status).ShouldBe([IdeaTradeRefusal.IdeaNotOpen]);
    }

    [TestCase(IdeaStatus.AwaitingEntry)]
    [TestCase(IdeaStatus.Active)]
    public void Idea_that_is_open_is_no_reason(IdeaStatus status)
    {
        Refuse(status: status).ShouldBeEmpty();
    }

    // Scenario "Цена ушла за цель".
    [Test]
    public void Long_price_beyond_the_target_is_named()
    {
        Refuse(price: 24.50m).ShouldContain(IdeaTradeRefusal.PriceBeyondTarget);
    }

    [Test]
    public void Long_price_at_or_below_the_stop_is_named()
    {
        Refuse(price: 23.01m).ShouldContain(IdeaTradeRefusal.PriceBeyondStop);
        Refuse(price: 22.90m).ShouldContain(IdeaTradeRefusal.PriceBeyondStop);
    }

    // A pullback enters at the price of its idea, but the market is where it is: it may be beyond the stop or the target already.
    [Test]
    public void Pullback_is_named_by_the_price_of_the_market_not_by_its_own_entry()
    {
        Refuse(price: 23.48m, current: 22.90m).ShouldContain(IdeaTradeRefusal.PriceBeyondStop);
        Refuse(price: 23.48m, current: 24.50m).ShouldContain(IdeaTradeRefusal.PriceBeyondTarget);
        Refuse(price: 23.48m, current: 23.60m).ShouldBeEmpty();
    }

    [Test]
    public void Short_price_beyond_the_levels_is_named()
    {
        var plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Short, 100m, 102m, 96m);

        Refuse(plan: plan, price: 95m).ShouldContain(IdeaTradeRefusal.PriceBeyondTarget);
        Refuse(plan: plan, price: 102.5m).ShouldContain(IdeaTradeRefusal.PriceBeyondStop);
    }

    // Scenario "R:R упал".
    [Test]
    public void Risk_reward_below_the_lowest_is_named()
    {
        Refuse(riskReward: 1.2m).ShouldBe([IdeaTradeRefusal.RiskRewardTooLow]);
    }

    [Test]
    public void Risk_reward_equal_to_the_lowest_is_no_reason()
    {
        Refuse(riskReward: 1.5m).ShouldBeEmpty();
    }

    [Test]
    public void As_many_open_trades_as_allowed_is_named()
    {
        Refuse(open: new OpenIdeaTrades(5, 1m)).ShouldBe([IdeaTradeRefusal.TooManyOpenTrades]);
        Refuse(open: new OpenIdeaTrades(4, 1m)).ShouldBeEmpty();
    }

    // Scenario "Общий риск": 3 × 1.5 + 1 = 5.5 > 5.
    [Test]
    public void Total_risk_above_the_limit_is_named()
    {
        Refuse(open: new OpenIdeaTrades(3, 4.5m), risk: 1m).ShouldBe([IdeaTradeRefusal.TotalRiskExceeded]);
    }

    [Test]
    public void Total_risk_equal_to_the_limit_is_no_reason()
    {
        Refuse(open: new OpenIdeaTrades(3, 4m), risk: 1m).ShouldBeEmpty();
    }

    // Scenario "Противоположный бот".
    [Test]
    public void Opposite_side_on_the_symbol_of_linear_is_named()
    {
        Refuse(account: Account(directions: [StrategyDirection.Short])).ShouldBe([IdeaTradeRefusal.OppositeSideOpen]);
    }

    // Scenario "Та же сторона".
    [Test]
    public void Same_side_on_the_symbol_is_no_reason()
    {
        Refuse(account: Account(directions: [StrategyDirection.Long])).ShouldBeEmpty();
    }

    [Test]
    public void Opposite_side_does_not_matter_on_the_spot()
    {
        Refuse(category: MarketCategory.Spot, account: Account(directions: [StrategyDirection.Short])).ShouldNotContain(IdeaTradeRefusal.OppositeSideOpen);
    }

    // Decision 9: the spot waits for the check of the real exchange, so the session cannot yet tell its TP/SL orders from the others.
    [Test]
    public void Spot_is_named_until_its_children_can_be_told()
    {
        Refuse(category: MarketCategory.Spot).ShouldBe([IdeaTradeRefusal.SpotNotSupported]);
    }

    // A stop entry waits for the price to leave the range of its setup; taken now it would enter at the market inside the range.
    [Test]
    public void Stop_entry_is_named_until_it_goes_as_a_stop_order()
    {
        Refuse(plan: Long with { Type = IdeaType.StopEntry }).ShouldBe([IdeaTradeRefusal.StopEntryNotSupported]);
    }

    [Test]
    public void Short_idea_meets_a_long_bot_as_the_opposite_side()
    {
        var plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Short, 23.48m, 23.95m, 22.54m);

        Refuse(plan: plan, account: Account(directions: [StrategyDirection.Long])).ShouldBe([IdeaTradeRefusal.OppositeSideOpen]);
    }

    [Test]
    public void Free_money_below_the_amount_is_named()
    {
        Refuse(account: Account(free: 46m), amount: 47m).ShouldBe([IdeaTradeRefusal.NotEnoughMoney]);
    }

    // The bot keeps 1000 − 300 invested = 700 free for itself, the trade that waits for its entry 100 more.
    [Test]
    public void Free_money_is_counted_with_the_bots_and_the_trades_that_wait()
    {
        Refuse(account: Account(free: 846m, bots: [new BotFunds(1000m, 300m)], pending: [100m]), amount: 47m).ShouldBe([IdeaTradeRefusal.NotEnoughMoney]);
        Refuse(account: Account(free: 847m, bots: [new BotFunds(1000m, 300m)], pending: [100m]), amount: 47m).ShouldBeEmpty();
    }

    [Test]
    public void Quantity_below_the_minimum_is_named()
    {
        Refuse(quantity: 0.09m, amount: 5.5m).ShouldBe([IdeaTradeRefusal.QuantityBelowMinimum]);
    }

    [Test]
    public void Amount_below_the_minimum_is_named()
    {
        Refuse(quantity: 0.2m, amount: 4.9m).ShouldBe([IdeaTradeRefusal.AmountBelowMinimum]);
    }

    [TestCase(AccountAccess.NoKey, IdeaTradeRefusal.NoKey)]
    [TestCase(AccountAccess.CategoryNotTraded, IdeaTradeRefusal.CategoryNotTraded)]
    [TestCase(AccountAccess.HedgeMode, IdeaTradeRefusal.HedgeMode)]
    public void What_the_account_cannot_do_is_named(AccountAccess access, IdeaTradeRefusal expected)
    {
        Refuse(account: Account(access: access)).ShouldBe([expected]);
    }

    private static IReadOnlyList<IdeaTradeRefusal> Refuse(
        MarketCategory category = MarketCategory.Linear,
        IdeaStatus status = IdeaStatus.AwaitingEntry,
        IdeaPlan? plan = null,
        decimal price = 23.5m,
        decimal? current = null,
        decimal quantity = 2m,
        decimal amount = 47m,
        decimal risk = 1m,
        decimal riskReward = 1.84m,
        OpenIdeaTrades? open = null,
        IdeaTradeAccount? account = null)
    {
        var entry = new IdeaTradeEntry(category, status, plan ?? Long, price, current ?? price, new IdeaTradeSize(quantity, amount, risk, riskReward, false), Rules);

        return IdeaTradeRefusals.Check(entry, Limits, open ?? new OpenIdeaTrades(0, 0m), account ?? Account());
    }

    private static IdeaTradeAccount Account(
        AccountAccess access = AccountAccess.Ready,
        decimal free = 1000m,
        IReadOnlyCollection<BotFunds>? bots = null,
        IReadOnlyCollection<decimal>? pending = null,
        IReadOnlyCollection<StrategyDirection>? directions = null)
    {
        return new IdeaTradeAccount(access, free, bots ?? [], pending ?? [], directions ?? []);
    }
}