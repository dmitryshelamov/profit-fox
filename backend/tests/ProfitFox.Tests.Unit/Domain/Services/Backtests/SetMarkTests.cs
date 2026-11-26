using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests.Enums;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>
///     Spec strategy-backtest «Условия отметки набора» and «Отметка набора» (change fix-backtest-random-control, decisions
///     10–12, task 5.3): the five conditions on the numbers of a set and its mark by the kind of the strategy, the engine
///     version and whether the backtest is on the rest of history.
/// </summary>
public sealed class SetMarkTests
{
    // «Все пять условий»
    [Test]
    public void All_five_conditions()
    {
        var conditions = MarkConditions.Of(Numbers(weeks: 141, mean: 0.004, up: 80, without: 900, difference: 0.005, t: 2.6));

        Assert.That(conditions, Is.EqualTo(new MarkConditions(true, true, true, true, true)));
        Assert.That(conditions.All, Is.True);
    }

    // «Ровно половина недель и t ровно два»
    [Test]
    public void Exactly_half_the_weeks_and_a_t_of_exactly_two()
    {
        var conditions = MarkConditions.Of(Numbers(weeks: 28, mean: 0.001, up: 14, without: 10, difference: 0.002, t: 2.0));

        Assert.That((conditions.WeeksUp, conditions.Difference), Is.EqualTo((true, true)));
    }

    // «Числа нет»
    [Test]
    public void A_condition_without_its_number_is_not_met()
    {
        var conditions = MarkConditions.Of(Numbers(weeks: 3, mean: 0.01, up: 3, without: null, difference: 0.01, t: 3));

        Assert.That(conditions.WithoutBest, Is.False);
    }

    // «Разница без t»
    [Test]
    public void A_difference_without_t_does_not_meet_the_fifth_condition()
    {
        var conditions = MarkConditions.Of(Numbers(weeks: 30, mean: 0.01, up: 20, without: 10, difference: 0.01, t: null));

        Assert.That(conditions.Difference, Is.False);
    }

    // «Кандидат на подборе»
    [Test]
    public void A_candidate_of_a_grid()
    {
        SetMark mark = Mark(Numbers(weeks: 141, mean: 0.004, up: 80, without: 900, difference: 0.005, t: 2.6), onRest: false)!;

        Assert.That(mark.Verdict, Is.EqualTo(SetVerdict.Candidate));
        Assert.That(mark.Conditions.All, Is.True);
    }

    // «Подбор без отметки»
    [Test]
    public void A_set_of_a_grid_without_a_mark()
    {
        SetMark mark = Mark(Numbers(weeks: 141, mean: 0.004, up: 80, without: 900, difference: 0.003, t: 1.4), onRest: false)!;

        Assert.That(mark.Verdict, Is.Null);
        Assert.That(mark.Conditions, Is.EqualTo(new MarkConditions(true, true, true, true, Difference: false)));
    }

    // «Прошёл на остатке»
    [Test]
    public void Passed_on_the_rest()
    {
        SetMark mark = Mark(Numbers(weeks: 28, mean: 0.006, up: 17, without: 250, difference: 0.007, t: 2.3), onRest: true)!;

        Assert.That(mark.Verdict, Is.EqualTo(SetVerdict.Passed));
    }

    // «Плюс на сделку при минусе по неделям»
    [Test]
    public void Up_by_trade_and_down_by_weeks_on_the_rest_failed()
    {
        SetSummary numbers = Numbers(weeks: 28, mean: -0.0014, up: 10, without: -1460, difference: 0.0002, t: 0.1) with { Mean = 0.0024 };

        Assert.That(Mark(numbers, onRest: true)!.Verdict, Is.EqualTo(SetVerdict.Failed));
    }

    // «Разница ниже нуля»
    [Test]
    public void A_difference_below_zero_on_the_rest_failed()
    {
        SetMark mark = Mark(Numbers(weeks: 28, mean: 0.003, up: 17, without: 250, difference: -0.001, t: -0.8), onRest: true)!;

        Assert.That(mark.Verdict, Is.EqualTo(SetVerdict.Failed));
    }

    // «Мало недель»
    [Test]
    public void Few_weeks_on_the_rest_unproven()
    {
        SetMark mark = Mark(Numbers(weeks: 12, mean: 0.003, up: 9, without: 50, difference: 0.008, t: 2.5), onRest: true)!;

        Assert.That(mark.Verdict, Is.EqualTo(SetVerdict.Unproven));
        Assert.That(mark.Conditions, Is.EqualTo(new MarkConditions(Weeks: false, true, true, true, true)));
    }

    // «Разница не доказана»
    [Test]
    public void An_unproven_difference_on_the_rest()
    {
        SetMark mark = Mark(Numbers(weeks: 28, mean: 0.003, up: 17, without: 250, difference: 0.003, t: 1.2), onRest: true)!;

        Assert.That(mark.Verdict, Is.EqualTo(SetVerdict.Unproven));
    }

    // «Плюс держится на лучших неделях»
    [Test]
    public void Up_on_the_best_weeks_only_on_the_rest_unproven()
    {
        SetMark mark = Mark(Numbers(weeks: 28, mean: 0.003, up: 15, without: -1460, difference: 0.004, t: 2.4), onRest: true)!;

        Assert.That(mark.Verdict, Is.EqualTo(SetVerdict.Unproven));
        Assert.That(mark.Conditions.WithoutBest, Is.False);
    }

    // «Остаток без сделок»
    [Test]
    public void The_rest_without_trades_unproven_with_no_condition_met()
    {
        SetMark mark = SetMark.Of(SetSummary.OfSet(SetNumbers.Zero), StrategyKind.Idea, engineVersion: 5, onRest: true)!;

        Assert.That(mark.Verdict, Is.EqualTo(SetVerdict.Unproven));
        Assert.That(mark.Conditions, Is.EqualTo(new MarkConditions(false, false, false, false, false)));
    }

    // «Лесенка без отметки»
    [Test]
    public void A_strategy_for_a_bot_has_no_mark()
    {
        SetSummary numbers = Numbers(weeks: 141, mean: 0.004, up: 80, without: 900, difference: 0.005, t: 2.6);

        Assert.That(SetMark.Of(numbers, StrategyKind.Bot, engineVersion: 5, onRest: false), Is.Null);
        Assert.That(SetMark.Of(numbers, StrategyKind.Bot, engineVersion: 5, onRest: true), Is.Null);
    }

    // «Прогон версии 4 без отметки»
    [Test]
    public void A_backtest_of_engine_version_4_has_no_mark()
    {
        SetSummary numbers = Numbers(weeks: 141, mean: 0.004, up: 80, without: 900, difference: 0.005, t: 2.6);

        Assert.That(SetMark.Of(numbers, StrategyKind.Idea, engineVersion: 4, onRest: true), Is.Null);
        Assert.That(SetMark.Of(numbers, StrategyKind.Idea, engineVersion: null, onRest: true), Is.Null, "a backtest not done yet");
    }

    [Test]
    public void Nineteen_weeks_are_too_few_and_twenty_enough()
    {
        Assert.That(MarkConditions.Of(Numbers(weeks: 19, mean: 0.01, up: 19, without: 1, difference: 0.01, t: 3)).Weeks, Is.False);
        Assert.That(MarkConditions.Of(Numbers(weeks: 20, mean: 0.01, up: 20, without: 1, difference: 0.01, t: 3)).Weeks, Is.True);
    }

    [Test]
    public void Thirteen_weeks_up_of_28_are_too_few_and_fourteen_enough()
    {
        Assert.That(MarkConditions.Of(Numbers(weeks: 28, mean: 0.01, up: 13, without: 1, difference: 0.01, t: 3)).WeeksUp, Is.False);
        Assert.That(MarkConditions.Of(Numbers(weeks: 28, mean: 0.01, up: 14, without: 1, difference: 0.01, t: 3)).WeeksUp, Is.True);
    }

    [Test]
    public void A_t_of_1_99_is_too_small_and_2_0_enough()
    {
        Assert.That(MarkConditions.Of(Numbers(weeks: 28, mean: 0.01, up: 20, without: 1, difference: 0.01, t: 1.99)).Difference, Is.False);
        Assert.That(MarkConditions.Of(Numbers(weeks: 28, mean: 0.01, up: 20, without: 1, difference: 0.01, t: 2.0)).Difference, Is.True);
    }

    [Test]
    public void A_sum_without_the_best_weeks_of_exactly_zero_is_not_above_zero()
    {
        Assert.That(MarkConditions.Of(Numbers(weeks: 28, mean: 0.01, up: 20, without: 0, difference: 0.01, t: 3)).WithoutBest, Is.False);
    }

    // The numbers of a set with the numbers by weeks and the difference given.
    private static SetSummary Numbers(int weeks, double mean, int up, double? without, double difference, double? t)
    {
        var summary = new SetSummary(weeks, weeks, 3, mean, 100, 0.5, Exclusions.None, 0, 0, 0, new RandomDifference(difference, weeks, t));
        return summary with { Weekly = new WeeklyNumbers(weeks, mean, up, Math.Max(3, (weeks + 9) / 10), without) };
    }

    private static SetMark? Mark(SetSummary numbers, bool onRest)
    {
        return SetMark.Of(numbers, StrategyKind.Idea, engineVersion: 5, onRest);
    }
}