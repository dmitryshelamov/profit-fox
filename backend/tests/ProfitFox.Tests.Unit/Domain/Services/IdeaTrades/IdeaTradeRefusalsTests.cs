using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.IdeaTrades;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.LiveTrading;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.IdeaTrades;

/// <summary>Requirement "Когда вход отклоняется": every reason that stops an entry has a test of its own.</summary>
public class IdeaTradeRefusalsTests
{
    private static readonly IdeaTradingLimits Limits = new IdeaTradingLimits(1.5m, 5, 5m, 200m);
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

    // Scenario "Сумма упёрлась в предел": the amount the owner asked is checked, not the one the rounded volume costs.
    [Test]
    public void Asked_amount_above_the_limit_is_named()
    {
        Refuse(asked: 250m).ShouldBe([IdeaTradeRefusal.AmountAboveMax]);
        Refuse(asked: 200.01m).ShouldBe([IdeaTradeRefusal.AmountAboveMax]);
    }

    [Test]
    public void Asked_amount_equal_to_the_limit_is_no_reason()
    {
        Refuse(asked: 200m).ShouldBeEmpty();
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

    // Decision 9: the owner opened the spot without the check of the real exchange.
    [Test]
    public void Spot_is_no_reason()
    {
        Refuse(category: MarketCategory.Spot).ShouldBeEmpty();
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

    // Scenario "Денег хватает на залог": 100 USDT at 5× takes 20 of the free money.
    [Test]
    public void Free_money_is_counted_against_the_margin_of_the_entry()
    {
        Refuse(account: Account(free: 30m), amount: 100m, asked: 100m, leverage: 5).ShouldBeEmpty();
        Refuse(account: Account(free: 19m), amount: 100m, asked: 100m, leverage: 5).ShouldBe([IdeaTradeRefusal.NotEnoughMoney]);
    }

    // Scenario "Ждущая сделка занимает свой залог": 20 of this entry and 25 of the trade that waits are more than 40.
    [Test]
    public void Margin_of_a_trade_that_waits_is_counted_with_the_margin_of_the_entry()
    {
        Refuse(account: Account(free: 40m, pending: [25m]), amount: 100m, asked: 100m, leverage: 5).ShouldBe([IdeaTradeRefusal.NotEnoughMoney]);
        Refuse(account: Account(free: 45m, pending: [25m]), amount: 100m, asked: 100m, leverage: 5).ShouldBeEmpty();
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

    // Scenario "Стоп дальше ликвидации": the stop is 12 % away, and 5× allows less than 1 / (2 × 5) = 10 %.
    [Test]
    public void Stop_beyond_half_the_way_to_the_liquidation_is_named()
    {
        var plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 88m, 130m);

        Refuse(plan: plan, price: 100m, leverage: 5).ShouldBe([IdeaTradeRefusal.StopTooFarForLeverage]);
    }

    [Test]
    public void Stop_exactly_at_half_the_way_to_the_liquidation_is_named()
    {
        var plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 90m, 130m);

        Refuse(plan: plan, price: 100m, leverage: 5).ShouldBe([IdeaTradeRefusal.StopTooFarForLeverage]);
    }

    // Scenario "Стоп при плече 1×": 40 % is less than 50 %.
    [Test]
    public void Stop_within_half_the_way_to_the_liquidation_is_no_reason()
    {
        var plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Short, 100m, 140m, 80m);

        Refuse(plan: plan, price: 100m, leverage: 1).ShouldBeEmpty();
    }

    [Test]
    public void Stop_far_from_the_entry_does_not_matter_on_the_spot()
    {
        var plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 40m, 130m);

        Refuse(category: MarketCategory.Spot, plan: plan, price: 100m).ShouldBeEmpty();
    }

    // Scenario "Чужая позиция с другим плечом": the leverage of a position that is not ours never changes.
    [Test]
    public void Position_on_the_symbol_with_another_leverage_is_named()
    {
        Refuse(account: Account(symbol: new LinearSymbol(1m, 0.5m, 0, [])), leverage: 5).ShouldBe([IdeaTradeRefusal.LeverageDiffers]);
    }

    // Scenario "Открытый ордер с другим плечом": the order would fill at the new leverage with a stop nobody checked against it.
    [Test]
    public void Open_order_on_the_symbol_with_another_leverage_is_named()
    {
        Refuse(account: Account(symbol: new LinearSymbol(1m, 0m, 1, [])), leverage: 5).ShouldBe([IdeaTradeRefusal.LeverageDiffers]);
    }

    // Scenario "Пустой символ с другим плечом": the entry sets the leverage.
    [Test]
    public void Empty_symbol_with_another_leverage_is_no_reason()
    {
        Refuse(account: Account(symbol: new LinearSymbol(10m, 0m, 0, [])), leverage: 1).ShouldBeEmpty();
    }

    // Scenario "Ждущая сделка на символе с другим плечом": trade A was saved at 1× and sent nothing yet, the exchange shows an empty symbol at 1×,
    // and the setting is 5× now.
    [Test]
    public void Trade_that_waits_on_the_symbol_holds_it_though_the_exchange_shows_nothing()
    {
        Refuse(account: Account(symbol: new LinearSymbol(1m, 0m, 0, [1])), leverage: 5).ShouldBe([IdeaTradeRefusal.LeverageDiffers]);
    }

    // The exchange shows the setting, but a trade on the symbol entered at another leverage it knows.
    [Test]
    public void Trade_on_the_symbol_at_another_leverage_is_named_though_the_exchange_shows_the_setting()
    {
        Refuse(account: Account(symbol: new LinearSymbol(5m, 0m, 0, [1])), leverage: 5).ShouldBe([IdeaTradeRefusal.LeverageDiffers]);
    }

    [Test]
    public void Position_on_the_symbol_with_the_same_leverage_is_no_reason()
    {
        Refuse(account: Account(symbol: new LinearSymbol(5m, -0.5m, 2, [])), leverage: 5).ShouldBeEmpty();
    }

    // Scenarios "R:R ниже минимума" and "Не хватает денег" of the autos: the numbers the first reason is about.
    [Test]
    public void Reasons_with_numbers_tell_them()
    {
        var entry = new IdeaTradeEntry(MarketCategory.Linear, IdeaStatus.Active, Long, 23.5m, 23.5m, 300m, new IdeaTradeSize(2m, 47m, 1m, 1.08m, 1.08m), Rules);
        var open = new OpenIdeaTrades(8, 4.5m);
        IdeaTradeAccount account = Account(free: 212.40m);

        RefusalDetail? Detail(IdeaTradeRefusal reason) => IdeaTradeRefusals.DetailOf(reason, entry, Limits, open, account);

        Detail(IdeaTradeRefusal.RiskRewardTooLow).ShouldBe(RefusalDetail.Numbers(1.08m, 1.5m));
        Detail(IdeaTradeRefusal.NotEnoughMoney).ShouldBe(RefusalDetail.Numbers(212.40m, 300m));
        Detail(IdeaTradeRefusal.AmountAboveMax).ShouldBe(RefusalDetail.Numbers(300m, 200m));
        Detail(IdeaTradeRefusal.TooManyOpenTrades).ShouldBe(RefusalDetail.Numbers(8m, 5m));
        Detail(IdeaTradeRefusal.TotalRiskExceeded).ShouldBe(RefusalDetail.Numbers(5.5m, 5m));
    }

    // Scenario "Стоп слишком далеко": the share of the stop and its limit in percent.
    [Test]
    public void Stop_too_far_for_the_leverage_tells_the_share_of_the_stop_and_its_limit_in_percent()
    {
        var plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 88m, 130m);
        var entry = new IdeaTradeEntry(MarketCategory.Linear, IdeaStatus.Active, plan, 100m, 100m, 47m, new IdeaTradeSize(0.47m, 47m, 5.7m, 14m, 2.4m), Rules, 5);

        IdeaTradeRefusals.DetailOf(IdeaTradeRefusal.StopTooFarForLeverage, entry, Limits, new OpenIdeaTrades(0, 0m), Account()).ShouldBe(RefusalDetail.Numbers(12m, 10m));
    }

    // «На символе открыта позиция с плечом 1×»: the leverage of the symbol and the one of the settings.
    [Test]
    public void Leverage_that_differs_tells_the_leverage_of_the_symbol_and_of_the_entry()
    {
        var entry = new IdeaTradeEntry(MarketCategory.Linear, IdeaStatus.Active, Long, 23.5m, 23.5m, 47m, new IdeaTradeSize(2m, 47m, 1m, 1.84m, 1.84m), Rules, 5);
        IdeaTradeAccount account = Account(symbol: new LinearSymbol(1m, 0.5m, 0, []));

        IdeaTradeRefusals.DetailOf(IdeaTradeRefusal.LeverageDiffers, entry, Limits, new OpenIdeaTrades(0, 0m), account).ShouldBe(RefusalDetail.Numbers(1m, 5m));
    }

    // The leverage that differs is told: here that of the trade on the symbol, the exchange shows the setting.
    [Test]
    public void Leverage_that_differs_tells_the_leverage_of_a_trade_on_the_symbol_when_the_exchange_has_the_setting()
    {
        var entry = new IdeaTradeEntry(MarketCategory.Linear, IdeaStatus.Active, Long, 23.5m, 23.5m, 47m, new IdeaTradeSize(2m, 47m, 1m, 1.84m, 1.84m), Rules, 5);
        IdeaTradeAccount account = Account(symbol: new LinearSymbol(5m, 0m, 0, [null, 1]));

        IdeaTradeRefusals.DetailOf(IdeaTradeRefusal.LeverageDiffers, entry, Limits, new OpenIdeaTrades(0, 0m), account).ShouldBe(RefusalDetail.Numbers(1m, 5m));
    }

    [Test]
    public void Reasons_without_numbers_tell_nothing()
    {
        var entry = new IdeaTradeEntry(MarketCategory.Linear, IdeaStatus.Active, Long, 23.5m, 23.5m, 300m, new IdeaTradeSize(2m, 47m, 1m, 1.08m, 1.08m), Rules);

        IdeaTradeRefusals.DetailOf(IdeaTradeRefusal.HedgeMode, entry, Limits, new OpenIdeaTrades(0, 0m), Account()).ShouldBeNull();
        IdeaTradeRefusals.DetailOf(IdeaTradeRefusal.LeverageNotSet, entry, Limits, new OpenIdeaTrades(0, 0m), Account()).ShouldBeNull();
    }

    private static IReadOnlyList<IdeaTradeRefusal> Refuse(
        MarketCategory category = MarketCategory.Linear,
        IdeaStatus status = IdeaStatus.AwaitingEntry,
        IdeaPlan? plan = null,
        decimal price = 23.5m,
        decimal? current = null,
        decimal quantity = 2m,
        decimal amount = 47m,
        decimal asked = 47.3m,
        decimal risk = 1m,
        decimal riskReward = 1.84m,
        OpenIdeaTrades? open = null,
        IdeaTradeAccount? account = null,
        int leverage = 1)
    {
        var entry = new IdeaTradeEntry(category, status, plan ?? Long, price, current ?? price, asked, new IdeaTradeSize(quantity, amount, risk, risk * riskReward, riskReward), Rules, leverage);

        return IdeaTradeRefusals.Check(entry, Limits, open ?? new OpenIdeaTrades(0, 0m), account ?? Account());
    }

    private static IdeaTradeAccount Account(
        AccountAccess access = AccountAccess.Ready,
        decimal free = 1000m,
        IReadOnlyCollection<BotFunds>? bots = null,
        IReadOnlyCollection<decimal>? pending = null,
        IReadOnlyCollection<StrategyDirection>? directions = null,
        LinearSymbol? symbol = null)
    {
        return new IdeaTradeAccount(access, free, bots ?? [], pending ?? [], directions ?? [], symbol);
    }
}