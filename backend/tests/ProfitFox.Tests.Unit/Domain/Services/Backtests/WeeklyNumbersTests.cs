using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>
///     Spec strategy-backtest «Числа набора по неделям» and «Сумма без лучших недель» (change fix-backtest-random-control,
///     task 5.2): the numbers of a set by weeks from the sums of its weeks.
/// </summary>
public sealed class WeeklyNumbersTests
{
    [Test]
    public void Two_weeks()
    {
        // In the first week trades of +2 % and -1 %, in the second one of +1 %.
        WeeklyNumbers weekly = WeeklyNumbers.Of([new WeekTrades(1, 0.02 - 0.01, 2, 1), new WeekTrades(2, 0.01, 1, 1)])!;

        Assert.That((weekly.Weeks, weekly.WeeksUp), Is.EqualTo((2, 2)));
        Assert.That(weekly.Mean, Is.EqualTo(0.0075).Within(1e-15));
    }

    [Test]
    public void Trades_gathered_in_one_week()
    {
        // 100 trades of +1 % in one week, one trade of -1 % in each of three others.
        WeekTrades[] weeks = [new WeekTrades(1, 100 * 0.01, 100, 100), new WeekTrades(2, -0.01, 1, -1), new WeekTrades(3, -0.01, 1, -1), new WeekTrades(4, -0.01, 1, -1)];
        var set = new SetNumbers { Trades = 103, NetSum = weeks.Sum(week => week.NetSum), Weeks = weeks };

        WeeklyNumbers weekly = SetSummary.OfSet(set).Weekly!;

        Assert.That(set.Mean, Is.EqualTo(0.0094).Within(0.00005), "+0.94 % a trade");
        Assert.That(weekly.Mean, Is.EqualTo(-0.005).Within(1e-15));
        Assert.That((weekly.WeeksUp, weekly.Weeks), Is.EqualTo((1, 4)));
    }

    [Test]
    public void The_sum_without_the_best_weeks()
    {
        // 28 weeks: the three best gave +400, +300 and +200 USDT, the other 25 together -150 USDT.
        List<WeekTrades> weeks = [new WeekTrades(1, 0.04, 1, 400), new WeekTrades(2, 0.03, 1, 300), new WeekTrades(3, 0.02, 1, 200)];
        weeks.AddRange(Enumerable.Range(4, 25).Select(week => new WeekTrades(week, -0.0006, 1, -6)));

        WeeklyNumbers weekly = WeeklyNumbers.Of(weeks)!;

        Assert.That(weekly.Weeks, Is.EqualTo(28));
        Assert.That(weekly.BestWeeks, Is.EqualTo(3));
        Assert.That(weekly.WithoutBest, Is.EqualTo(-150).Within(1e-9));
    }

    [Test]
    public void Forty_one_weeks_have_five_best()
    {
        WeeklyNumbers weekly = WeeklyNumbers.Of([.. Enumerable.Range(1, 41).Select(week => new WeekTrades(week, 0.01, 1, week))])!;

        Assert.That(weekly.BestWeeks, Is.EqualTo(5));
        Assert.That(weekly.WithoutBest, Is.EqualTo(Enumerable.Range(1, 36).Sum()).Within(1e-9), "the weeks of 37 to 41 USDT are the best");
    }

    [Test]
    public void With_no_more_weeks_than_best_ones_there_is_no_sum_without_them()
    {
        WeeklyNumbers weekly = WeeklyNumbers.Of([new WeekTrades(1, 0.01, 1, 1), new WeekTrades(2, 0.01, 1, 2), new WeekTrades(3, 0.01, 1, 3)])!;

        Assert.That((weekly.Weeks, weekly.BestWeeks, weekly.WithoutBest), Is.EqualTo((3, 3, (double?)null)));
    }

    [Test]
    public void A_set_without_trades_has_no_numbers_by_weeks()
    {
        Assert.That(WeeklyNumbers.Of([]), Is.Null);
        Assert.That(SetSummary.OfSet(SetNumbers.Zero).Weekly, Is.Null);
    }

    [Test]
    public void A_week_whose_mean_is_exactly_zero_is_not_up()
    {
        WeeklyNumbers weekly = WeeklyNumbers.Of([new WeekTrades(1, 0.01 - 0.01, 2, 0), new WeekTrades(2, 0.01, 1, 1)])!;

        Assert.That(weekly.WeeksUp, Is.EqualTo(1));
    }

    [Test]
    public void Of_two_weeks_with_the_same_sum_the_earlier_is_the_better()
    {
        // Four weeks, three best: weeks 2 and 4 have the same sum; week 2 is among the best, week 4 is left.
        WeeklyNumbers weekly = WeeklyNumbers.Of([new WeekTrades(1, 0.01, 1, 50), new WeekTrades(2, 0.01, 1, 10), new WeekTrades(3, 0.01, 1, 40), new WeekTrades(4, -0.01, 1, 10)])!;

        Assert.That(weekly.WithoutBest, Is.EqualTo(10));
        Assert.That(WeeklyNumbers.Of([new WeekTrades(4, -0.01, 1, 10), new WeekTrades(3, 0.01, 1, 40), new WeekTrades(2, 0.01, 1, 10), new WeekTrades(1, 0.01, 1, 50)]), Is.EqualTo(weekly), "whatever the order of the weeks given");
    }

    [Test]
    public void The_numbers_of_a_pair_of_a_set_have_no_numbers_by_weeks()
    {
        var pair = new SetNumbers { Trades = 1, NetSum = 0.01, Amount = 1, Weeks = [new WeekTrades(1, 0.01, 1, 1)] };

        Assert.That(SetSummary.Of(pair).Weekly, Is.Null);
        Assert.That(SetSummary.OfSet(pair).Weekly, Is.Not.Null);
    }
}