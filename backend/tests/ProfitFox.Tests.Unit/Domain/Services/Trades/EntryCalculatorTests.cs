using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas;

namespace ProfitFox.Tests.Unit.Domain.Services.Trades;

/// <summary>
///     Change take-idea-trades, task 3.3: the count of an entry and every check of design.md, decision 7, in its order (spec
///     idea-trades «Расчёт перед входом», «Условия входа по идее», «Размер сделки», «Плечо сделки», «Одна сделка на пару»,
///     «Только односторонний режим», «Только изолированная маржа», «Уровни сделки»; spec trade-risk).
/// </summary>
public sealed class EntryCalculatorTests
{
    private static readonly DateTimeOffset Now = IdeaGenerator.Noon.AddHours(1);

    private static readonly TradeSettings Settings = new TradeSettings
    {
        DefaultAmount = 100m,
        Leverage = 3,
        MaxAmount = 200m,
        MaxOpenTrades = 5,
        MaxTotalAmount = 1000m,
        MaxLossPercent = 3m,
        MaxDayLoss = 50m
    };

    private static readonly PairRules Sol = new PairRules("SOLUSDT", 0.1m, 0.1m, 5m, 0.01m, 50m, true);

    [Test]
    public void The_checks_go_in_the_order_of_the_dialog()
    {
        Assert.That(
            EntryCalculator.Count(Input()).Checks.Select(check => check.Code),
            Is.EqualTo(new[]
            {
                "limits", "halt", "trader", "idea", "time", "levels", "pair", "key", "margin", "oneWay", "pairTrade", "position", "amount", "open",
                "total", "minimum", "leverage", "liquidation", "free"
            }));
    }

    // Spec «Расчёт лонга».
    [Test]
    public void A_long_of_100_USDT_at_237_40_is_0_4_with_the_stop_2_54_percent_below_and_the_target_3_43_above()
    {
        EntryCalculation count = EntryCalculator.Count(Input());
        Assert.Multiple(() =>
        {
            Assert.That(count.Passed, Is.True, string.Join("; ", count.Checks.Where(check => !check.Passed).Select(check => check.Text)));
            Assert.That(count.Quantity, Is.EqualTo(0.4m));
            Assert.That(count.Value, Is.EqualTo(94.96m));
            Assert.That(count.Margin, Is.EqualTo(94.96m / 3));
            Assert.That(count.Stop, Is.EqualTo(231.38m));
            Assert.That(count.StopSource, Is.EqualTo(StopSource.Idea));
            Assert.That(count.LimitStop, Is.EqualTo(230.28m));
            Assert.That(decimal.Round(count.StopDistance!.Value * 100, 2), Is.EqualTo(-2.54m));
            Assert.That(count.Target, Is.EqualTo(245.54m));
            Assert.That(decimal.Round(count.TargetDistance!.Value * 100, 2), Is.EqualTo(3.43m));
            Assert.That(decimal.Round(count.LossAtStop!.Value, 2), Is.EqualTo(-2.51m));
            Assert.That(decimal.Round(count.ProfitAtTarget!.Value, 2), Is.EqualTo(3.15m));
            Assert.That(count.Until, Is.EqualTo(Idea().Until));
        });
    }

    // Spec «Количество по шагу».
    [Test]
    public void The_quantity_goes_down_to_the_step_of_the_pair()
    {
        EntryCalculation count = EntryCalculator.Count(Input());
        Assert.That((count.Quantity, decimal.Round(count.Value, 2)), Is.EqualTo((0.4m, 94.96m)));
    }

    // Spec «Меньше минимума».
    [Test]
    public void Five_USDT_of_BTC_is_below_the_minimum_of_the_exchange()
    {
        Idea idea = IdeaGenerator.Generate(symbol: "BTCUSDT", side: EntrySide.Long, moment: IdeaGenerator.Noon, close: 100000m);
        EntryCheck check = Checked(Input(idea: idea, amount: 5m, price: 100000m, rules: new PairRules("BTCUSDT", 0.001m, 0.001m, 5m, 0.1m, 100m, true)), "minimum");
        Assert.That(check, Is.EqualTo(new EntryCheck("minimum", false, "Сумма меньше минимума биржи для пары")));
    }

    // Spec «Плечо больше максимума пары».
    [Test]
    public void A_leverage_of_10_on_a_pair_of_5_at_most_is_refused()
    {
        EntryCheck check = Checked(Input(settings: Settings with { Leverage = 10, MaxLossPercent = 2m }, rules: Sol with { MaxLeverage = 5m }), "leverage");
        Assert.That(check.Text, Is.EqualTo("Плечо 10× больше максимума пары 5×"));
        Assert.That(check.Passed, Is.False);
    }

    // Spec «Цена прошла цель».
    [Test]
    public void A_long_whose_price_passed_the_target_is_refused()
    {
        Idea idea = Idea(entry: 95.12, stop: 93.22, target: 98.92, symbol: "LTCUSDT");
        EntryCheck check = Checked(Input(idea: idea, price: 99.02m, rules: Sol with { Symbol = "LTCUSDT" }), "levels");
        Assert.That(check, Is.EqualTo(new EntryCheck("levels", false, "Цена 99.02 уже выше цели идеи 98.92")));
    }

    // Spec «Цена прошла стоп».
    [Test]
    public void A_long_whose_price_passed_the_stop_is_refused()
    {
        EntryCheck check = Checked(Input(price: 231.10m), "levels");
        Assert.That(check, Is.EqualTo(new EntryCheck("levels", false, "Цена 231.1 уже ниже стопа идеи 231.38")));
    }

    [Test]
    public void A_short_whose_price_passed_its_stop_upward_is_refused()
    {
        Idea idea = Idea(side: EntrySide.Short, entry: 1.4051, stop: 1.43, target: 1.38, symbol: "XRPUSDT");
        Assert.That(Checked(Input(idea: idea, price: 1.44m, rules: Xrp), "levels").Passed, Is.False);
    }

    [Test]
    public void A_closed_idea_is_refused()
    {
        Idea idea = Idea();
        idea.State = IdeaState.Closed;
        Assert.That(Checked(Input(idea: idea), "idea"), Is.EqualTo(new EntryCheck("idea", false, "Идея закрыта")));
    }

    // Spec «Срок через полминуты».
    [Test]
    public void Half_a_minute_before_the_end_is_less_than_a_minute()
    {
        Idea idea = Idea();
        EntryCheck check = Checked(Input(idea: idea, now: idea.Until.AddSeconds(-30)), "time");
        Assert.That(check, Is.EqualTo(new EntryCheck("time", false, "До срока идеи меньше минуты")));
    }

    // Spec «Запас пять минут».
    [Test]
    public void Three_minutes_before_the_end_are_less_than_a_margin_of_five()
    {
        Idea idea = Idea();
        EntryCheck check = Checked(Input(idea: idea, settings: Settings with { ExpiryMargin = TimeSpan.FromMinutes(5) }, now: idea.Until.AddMinutes(-3)), "time");
        Assert.That(check, Is.EqualTo(new EntryCheck("time", false, "До срока идеи меньше 5 минут")));
    }

    [Test]
    public void A_pair_that_does_not_trade_is_refused()
    {
        Assert.That(Checked(Input(rules: Sol with { Trading = false }), "pair"), Is.EqualTo(new EntryCheck("pair", false, "SOLUSDT не торгуется на Bybit")));
        Assert.That(Checked(Input(noRules: true), "pair").Passed, Is.False);
    }

    // Spec trade-risk «Ключу включили вывод».
    [Test]
    public void A_key_that_may_withdraw_is_refused()
    {
        EntryCheck check = Checked(Input(account: Account() with { KeyProblem = "У ключа аккаунта есть право вывода" }), "key");
        Assert.That(check, Is.EqualTo(new EntryCheck("key", false, "У ключа аккаунта есть право вывода")));
    }

    // Spec «Вторая идея по той же паре».
    [Test]
    public void A_second_trade_on_the_pair_of_the_account_is_refused()
    {
        EntryCheck check = Checked(Input(counters: Counters() with { PairTaken = true }), "pairTrade");
        Assert.That(check, Is.EqualTo(new EntryCheck("pairTrade", false, "По паре уже есть сделка")));
    }

    // Spec «Своя позиция владельца».
    [Test]
    public void A_position_of_the_owner_on_the_pair_is_refused()
    {
        EntryAccount account = Account() with { Positions = [new ExchangePosition("SOLUSDT", TradeSide.Long, 2m, 230m, null, null, 1m, true, null, null)] };
        Assert.That(Checked(Input(account: account), "position"), Is.EqualTo(new EntryCheck("position", false, "По паре на аккаунте есть позиция не из стенда")));
    }

    [Test]
    public void An_empty_position_of_the_pair_is_no_position()
    {
        EntryAccount account = Account() with { Positions = [ExchangePosition.Empty("SOLUSDT")] };
        Assert.That(Checked(Input(account: account), "position").Passed, Is.True);
    }

    // Spec «Режим хеджа».
    [Test]
    public void A_pair_in_the_hedge_mode_is_refused()
    {
        EntryAccount account = Account() with { Positions = [ExchangePosition.Empty("SOLUSDT") with { OneWay = false }] };
        Assert.That(
            Checked(Input(account: account), "oneWay"),
            Is.EqualTo(new EntryCheck("oneWay", false, "Пара в режиме хеджа, переключите её на Bybit в односторонний режим")));
    }

    // Spec «Изолированная маржа», «Кросс-маржа», «Портфельная маржа».
    [TestCase("ISOLATED_MARGIN", true, "Аккаунт в режиме изолированной маржи")]
    [TestCase("REGULAR_MARGIN", false, "Аккаунт в режиме кросс-маржи, переключите его на Bybit на изолированную маржу")]
    [TestCase("PORTFOLIO_MARGIN", false, "Аккаунт в режиме портфельной маржи, переключите его на Bybit на изолированную маржу")]
    [TestCase("SOME_NEW_MODE", false, "Режим маржи аккаунта SOME_NEW_MODE стенд не знает")]
    public void Only_the_isolated_margin_is_taken(string mode, bool passed, string text)
    {
        Assert.That(Checked(Input(account: Account() with { MarginMode = mode }), "margin"), Is.EqualTo(new EntryCheck("margin", passed, text)));
    }

    // Spec «Сумма больше предела».
    [Test]
    public void An_amount_above_the_limit_of_a_trade_is_refused()
    {
        Assert.That(Checked(Input(amount: 250m), "amount"), Is.EqualTo(new EntryCheck("amount", false, "Сумма больше предела 200 USDT")));
    }

    // Spec «Пять из пяти» and «Проверка не прошла»: the count is given whole with the failed check.
    [Test]
    public void Five_open_trades_of_five_refuse_the_entry_and_the_count_is_still_whole()
    {
        EntryCalculation count = EntryCalculator.Count(Input(counters: Counters() with { OpenTrades = 5 }));
        Assert.That(count.Checks.Single(check => check.Code == "open"), Is.EqualTo(new EntryCheck("open", false, "Открыто 5 сделок из 5")));
        Assert.That(count.Quantity, Is.EqualTo(0.4m));
    }

    // Spec «Сделка закрылась».
    [Test]
    public void Four_open_trades_of_five_allow_the_entry()
    {
        Assert.That(Checked(Input(counters: Counters() with { OpenTrades = 4 }), "open"), Is.EqualTo(new EntryCheck("open", true, "Открытых сделок 4 из 5")));
    }

    // Spec «Не помещается».
    [Test]
    public void An_entry_beyond_the_limit_of_the_amount_in_all_is_refused()
    {
        EntryCheck check = Checked(Input(counters: Counters() with { OpenAmount = 950m }), "total");
        Assert.That(check, Is.EqualTo(new EntryCheck("total", false, "В сделках было бы 1 050 USDT при пределе 1 000")));
    }

    // Spec «Денег не хватает».
    [Test]
    public void Twenty_free_USDT_are_not_enough_for_a_margin_of_about_33()
    {
        EntryCheck check = Checked(Input(account: Account() with { Free = 20m }), "free");
        Assert.That(check, Is.EqualTo(new EntryCheck("free", false, "Свободных USDT не хватает: 20 из 31.76")));
    }

    // Spec «Плечо уменьшает залог».
    [Test]
    public void Forty_free_USDT_are_enough_at_3x()
    {
        Assert.That(Checked(Input(account: Account() with { Free = 40m }), "free").Passed, Is.True);
    }

    // Spec «Залог держат другие сделки»: what the other positions hold is not free.
    [Test]
    public void Thirty_free_USDT_of_a_hundred_are_not_enough()
    {
        Assert.That(Checked(Input(account: Account() with { Free = 30m }), "free").Passed, Is.False);
    }

    [Test]
    public void A_stop_beyond_half_the_way_to_the_liquidation_is_refused()
    {
        // The limit of the loss is wider than the rule of the settings allows, as if saved before the leverage grew.
        EntryCheck check = Checked(Input(settings: Settings with { Leverage = 10, MaxLossPercent = 6m }, idea: Idea(stop: null, target: null)), "liquidation");
        Assert.That(check.Passed, Is.False);
    }

    [Test]
    public void The_liquidation_is_estimated_at_the_leverage()
    {
        Assert.That(Checked(Input(), "liquidation"), Is.EqualTo(new EntryCheck("liquidation", true, "Стоп ближе ликвидации (≈ 159.5)")));
    }

    // Spec trade-risk «Свежий стенд».
    [Test]
    public void Without_limits_the_entry_is_refused_with_their_names()
    {
        EntryCheck check = Checked(Input(settings: TradeSettings.Empty), "limits");
        Assert.That(check.Passed, Is.False);
        Assert.That(check.Text, Does.StartWith("Задайте пределы в разделе «Риск»: плечо"));
    }

    [Test]
    public void The_emergency_stop_and_a_missing_trading_Worker_refuse_the_entry()
    {
        EntryCalculation count = EntryCalculator.Count(Input(counters: Counters() with { Halted = true, TraderAlive = false }));
        Assert.That(count.Checks.Single(check => check.Code == "halt"), Is.EqualTo(new EntryCheck("halt", false, "Вход запрещён стоп-краном")));
        Assert.That(count.Checks.Single(check => check.Code == "trader"), Is.EqualTo(new EntryCheck("trader", false, "Торговый Worker не работает")));
    }

    // Spec «Поздний клик»: the levels are those of the idea, not counted from the price of the click.
    [Test]
    public void A_late_click_keeps_the_stop_the_target_and_the_end_of_the_idea()
    {
        Idea idea = Idea(entry: 100000, stop: 98000, target: 104000, symbol: "BTCUSDT");
        EntryCalculation count = EntryCalculator.Count(Input(idea: idea, price: 101000m, settings: Settings with { MaxLossPercent = 5m }, rules: new PairRules("BTCUSDT", 0.001m, 0.001m, 5m, 0.1m, 100m, true)));
        Assert.That((count.Stop, count.Target, count.Until), Is.EqualTo(((decimal?)98000m, (decimal?)104000m, idea.Until)));
    }

    // Spec «Идея без цели»: no target, and the stop comes from the risk module.
    [Test]
    public void An_idea_without_a_stop_and_a_target_gets_the_stop_of_the_limit_and_no_target()
    {
        Idea idea = Idea(side: EntrySide.Short, entry: 1.4051, stop: null, target: null, symbol: "XRPUSDT");
        EntryCalculation count = EntryCalculator.Count(Input(idea: idea, price: 1.4126m, rules: Xrp));
        Assert.Multiple(() =>
        {
            Assert.That(count.Target, Is.Null);
            Assert.That(count.ProfitAtTarget, Is.Null);
            Assert.That(count.StopSource, Is.EqualTo(StopSource.Limit));
            Assert.That(count.Stop, Is.EqualTo(1.4549m));
            Assert.That(count.Quantity, Is.EqualTo(70m));
        });
    }

    // 12.12 (finding 12): a pair Bybit gives no price for is refused by its checks, not by a division by nought.
    [Test]
    public void A_pair_without_a_price_is_refused_and_counts_nothing_from_the_price()
    {
        EntryCalculation count = EntryCalculator.Count(Input(price: 0m));

        Assert.That(count.Checks.Single(check => check.Code == "pair"), Is.EqualTo(new EntryCheck("pair", false, "Цена SOLUSDT неизвестна: Bybit её не дал")));
        Assert.That(count.Checks.Single(check => check.Code == "levels").Passed, Is.False);
        Assert.That((count.Quantity, count.Stop, count.Passed), Is.EqualTo((0m, (decimal?)null, false)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProtectiveStop.Of(TradeSide.Long, 0m, null, 3m, 0.01m));
    }

    private static readonly PairRules Xrp = new PairRules("XRPUSDT", 1m, 1m, 5m, 0.0001m, 75m, true);

    private static EntryCheck Checked(EntryInput input, string code)
    {
        return EntryCalculator.Count(input).Checks.Single(check => check.Code == code);
    }

    private static EntryAccount Account()
    {
        return new EntryAccount(Guid.NewGuid(), "Основной", null, "ISOLATED_MARGIN", [], 1250.40m);
    }

    private static EntryCounters Counters()
    {
        return new EntryCounters(false, true, 2, 300m, false);
    }

    /// <summary>
    ///     A long on SOLUSDT entered at 236.10 with the stop at 231.38 and the target at 245.54, unless the test gives others.
    /// </summary>
    private static Idea Idea(EntrySide side = EntrySide.Long, double entry = 236.10, double? stop = 231.38, double? target = 245.54, string symbol = "SOLUSDT")
    {
        double direction = side == EntrySide.Long ? 1 : -1;
        Idea idea = IdeaGenerator.Generate(
            symbol: symbol,
            side: side,
            timeframe: TimeSpan.FromHours(1),
            stop: stop is { } s ? (1 - s / entry) * direction : null,
            target: target is { } t ? (t / entry - 1) * direction : null,
            close: (decimal)entry);
        idea.EntryPrice = entry;
        return idea;
    }

    private static EntryInput Input(
        Idea? idea = null,
        TradeSettings? settings = null,
        decimal amount = 100m,
        EntryAccount? account = null,
        decimal price = 237.40m,
        PairRules? rules = null,
        EntryCounters? counters = null,
        DateTimeOffset? now = null,
        bool noRules = false)
    {
        return new EntryInput(idea ?? Idea(), settings ?? Settings, amount, account ?? Account(), price, noRules ? null : rules ?? Sol, counters ?? Counters(), now ?? Now);
    }
}
