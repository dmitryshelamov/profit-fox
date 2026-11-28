using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Tests.Unit.Domain.Services.Trades;

/// <summary>
///     Change take-idea-trades, task 2.1: the rules of the settings of the risk module (spec trade-risk «Настройки риска»,
///     «Сроки и попытки», «Стоп ближе ликвидации», «Без пределов входа нет»).
/// </summary>
public sealed class TradeSettingsRulesTests
{
    private static readonly TradeSettings Full = new TradeSettings
    {
        DefaultAmount = 100m,
        Leverage = 3,
        MaxAmount = 200m,
        MaxOpenTrades = 5,
        MaxTotalAmount = 1000m,
        MaxLossPercent = 3m,
        MaxDayLoss = 50m
    };

    // Spec «Значения по умолчанию».
    [Test]
    public void A_fresh_bench_has_a_minute_thirty_seconds_and_three_attempts_and_needs_none_of_them_set()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TradeSettings.Empty.ExpiryMargin, Is.EqualTo(TimeSpan.FromMinutes(1)));
            Assert.That(TradeSettings.Empty.EntryWait, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(TradeSettings.Empty.StopAttempts, Is.EqualTo(3));
            Assert.That(TradeSettingsRules.Check(TradeSettings.Empty), Is.Empty);
            Assert.That(TradeSettingsRules.Missing(Full), Is.Empty);
        });
    }

    // Spec «Ожидание вне границ».
    [Test]
    public void A_wait_of_300_seconds_is_refused_at_its_field()
    {
        Assert.That(
            TradeSettingsRules.Check(Full with { EntryWait = TimeSpan.FromSeconds(300) }),
            Is.EqualTo(new[] { new SettingsProblem("entryWaitSeconds", "Range", "ждать приёма входа — от 10 до 120 секунд") }));
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(60, true)]
    [TestCase(61, false)]
    public void The_margin_before_the_end_is_from_1_to_60_minutes(int minutes, bool allowed)
    {
        Assert.That(TradeSettingsRules.Check(Full with { ExpiryMargin = TimeSpan.FromMinutes(minutes) }).Any(problem => problem.Field == "expiryMarginMinutes"), Is.EqualTo(!allowed));
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(10, true)]
    [TestCase(11, false)]
    public void The_attempts_to_put_the_stop_back_are_from_1_to_10(int attempts, bool allowed)
    {
        Assert.That(TradeSettingsRules.Check(Full with { StopAttempts = attempts }).Any(problem => problem.Field == "stopAttempts"), Is.EqualTo(!allowed));
    }

    [TestCase(9, false)]
    [TestCase(10, true)]
    [TestCase(120, true)]
    [TestCase(121, false)]
    public void The_wait_for_the_entry_is_from_10_to_120_seconds(int seconds, bool allowed)
    {
        Assert.That(TradeSettingsRules.Check(Full with { EntryWait = TimeSpan.FromSeconds(seconds) }).Any(problem => problem.Field == "entryWaitSeconds"), Is.EqualTo(!allowed));
    }

    // Spec «Плечо вне границ».
    [TestCase(25)]
    [TestCase(0)]
    public void A_leverage_outside_1_to_20_is_refused_at_the_leverage(int leverage)
    {
        Assert.That(
            TradeSettingsRules.Check(Full with { Leverage = leverage, MaxLossPercent = 1m }),
            Is.EqualTo(new[] { new SettingsProblem("leverage", "Range", "плечо — целое от 1 до 20") }));
    }

    // Spec «Сумма по умолчанию больше предела».
    [Test]
    public void A_default_amount_above_the_limit_of_a_trade_is_refused_at_the_default_amount()
    {
        Assert.That(
            TradeSettingsRules.Check(Full with { DefaultAmount = 300m }),
            Is.EqualTo(new[] { new SettingsProblem("defaultAmount", "AboveLimit", "сумма по умолчанию больше предела на сделку 200 USDT") }));
    }

    // Spec «Предел слишком широк для плеча».
    [Test]
    public void A_loss_of_6_percent_at_10x_is_refused_at_the_loss()
    {
        Assert.That(
            TradeSettingsRules.Check(Full with { Leverage = 10, MaxLossPercent = 6m }),
            Is.EqualTo(new[] { new SettingsProblem("maxLossPercent", "TooWide", "при плече 10× предел потери должен быть меньше 5 %") }));
    }

    [Test]
    public void A_loss_exactly_half_the_way_to_the_liquidation_is_refused()
    {
        Assert.That(TradeSettingsRules.Check(Full with { Leverage = 10, MaxLossPercent = 5m }).Single().Field, Is.EqualTo("maxLossPercent"));
    }

    // Spec «Допустимая пара».
    [Test]
    public void A_loss_of_3_percent_at_3x_is_allowed()
    {
        Assert.That(TradeSettingsRules.Check(Full), Is.Empty);
    }

    // Spec «Подсказка к пределу потери».
    [TestCase(3, "16.6")]
    [TestCase(10, "5")]
    [TestCase(1, "50")]
    [TestCase(7, "7.1")]
    public void Half_the_way_to_the_liquidation_is_written_cut_to_a_tenth(int leverage, string text)
    {
        Assert.That(TradeSettingsRules.HalfWayText(leverage), Is.EqualTo(text));
    }

    // Spec «Не задан убыток за день».
    [Test]
    public void Without_the_loss_of_a_day_the_missing_limit_is_named()
    {
        Assert.That(TradeSettingsRules.Missing(Full with { MaxDayLoss = null }), Is.EqualTo(new[] { "убыток за день" }));
    }

    // Spec «Свежий стенд».
    [Test]
    public void A_fresh_bench_misses_every_limit()
    {
        Assert.That(
            TradeSettingsRules.Missing(TradeSettings.Empty),
            Is.EqualTo(new[] { "плечо", "сумма на сделку", "число открытых сделок", "общая сумма в сделках", "потеря на сделку", "убыток за день" }));
    }

    [Test]
    public void Limits_not_above_nought_are_refused_each_at_its_field()
    {
        IReadOnlyList<SettingsProblem> problems = TradeSettingsRules.Check(new TradeSettings { DefaultAmount = 0m, MaxAmount = 0m, MaxOpenTrades = 0, MaxTotalAmount = -1m, MaxLossPercent = 0m, MaxDayLoss = 0m });
        Assert.That(problems.Select(problem => problem.Field), Is.EqualTo(new[] { "defaultAmount", "maxAmount", "maxOpenTrades", "maxTotalAmount", "maxLossPercent", "maxDayLoss" }));
    }
}
