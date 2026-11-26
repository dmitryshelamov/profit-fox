using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Graph;

using static ProfitFox.Tests.Unit.Domain.Services.Backtests.BacktestHistory;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>
///     Change add-strategy-backtest, task 4.3, and change fix-backtest-random-control, tasks 3.2, 3.4 and 3.5: a category
///     of ideas on one pair for all sets and over several pairs in two passes — spec strategy-backtest «Случайные входы»,
///     «Заявка при открытой позиции в прогоне», «Контрольная группа недели», «Повторяемость прогона» and spec market-history
///     «Минутные свечи в окнах сделок». The strategy is funding-short: an order at the close of the candle that first sees a
///     payment at or above the threshold. The history starts on Monday 2026-01-05, so a pair is in a control group from the
///     week of 2026-01-12.
/// </summary>
public sealed class PairBacktestTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);
    private static readonly long FirstWeek = Blocks.Of(Monday, TimeBlock.Week);

    [Test]
    public async Task Six_sets_read_the_minutes_of_each_week_once()
    {
        BacktestHistory history = new BacktestHistory().High(Monday.AddHours(12)).High(Monday.AddDays(2).AddHours(12)).High(Monday.AddDays(15).AddHours(4));
        IReadOnlyList<BacktestPlan> plans = history.Plans(Graphs.H0001Strategy, StrategyKind.Idea, [Thresholds("0.0003", "0.0005", "0.001")], Hour, FourHours);

        IReadOnlyList<PairSetResult> results = await history.Run(StrategyKind.Idea, plans);

        Assert.That(results.Select(result => result.Set), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
        Assert.That(results.Select(result => result.Numbers.Trades), Is.EqualTo(new[] { 3, 3, 3, 3, 0, 0 }), "a threshold of 0.001 is never reached");
        Assert.That(history.MinuteReads.Select(read => Blocks.Of(read.From, TimeBlock.Week)), Is.EqualTo(new[] { FirstWeek, FirstWeek + 2 }), "one read a week for every set");
        Assert.That(history.MinuteReads[0], Is.EqualTo((Monday, Monday.AddDays(7) + FourHours)), "the week and the longest time of its steps");
    }

    [Test]
    public async Task A_week_without_orders_of_any_set_reads_no_minutes()
    {
        BacktestHistory history = new BacktestHistory().High(Monday.AddHours(12)).High(Monday.AddDays(15).AddHours(4));

        await history.Run(StrategyKind.Idea, history.Plans(Graphs.H0001Strategy, StrategyKind.Idea, [Thresholds("0.0005")], FourHours));

        Assert.That(history.MinuteReads.Select(read => read.From), Has.None.InRange(Monday.AddDays(7), Monday.AddDays(14).AddTicks(-1)));
        Assert.That(history.MinuteReads, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task A_second_order_inside_the_time_of_the_first_is_skipped()
    {
        // Orders at the closes of 12:00 and 20:00: the cycle of the first runs to Tuesday 12:00.
        BacktestHistory history = new BacktestHistory().High(Monday.AddHours(12), low: Monday.AddHours(13)).High(Monday.AddHours(20));

        PairSetResult result = (await history.Run(StrategyKind.Idea, Plans(history, after: "1d"))).Single();

        Assert.That(result.Rows.Select(row => row.Moment), Is.EqualTo(new[] { Monday.AddHours(12) }), "the order at 20:00 has no row and no random entries");
        Assert.That((result.Numbers.Skipped, result.Numbers.Trades, result.Numbers.Orders), Is.EqualTo((1, 1, 2)));
    }

    [Test]
    public async Task An_order_with_a_trade_left_out_on_a_hole_is_excluded_and_not_skipped()
    {
        BacktestHistory history = new BacktestHistory { Hole = (Monday.AddHours(13), Monday.AddHours(13).AddMinutes(5)) }.High(Monday.AddHours(12));

        PairSetResult result = (await history.Run(StrategyKind.Idea, Plans(history))).Single();

        Assert.That(result.Rows.Single().Status, Is.EqualTo(TradeStatus.Coverage));
        Assert.That((result.Numbers.Excluded.Coverage, result.Numbers.Skipped, result.Numbers.Trades), Is.EqualTo((1, 0, 0)));
    }

    [Test]
    public async Task A_set_alone_and_among_six_gives_the_same_numbers()
    {
        BacktestHistory alone = new BacktestHistory().High(Monday.AddHours(12)).High(Monday.AddDays(2).AddHours(12)).High(Monday.AddDays(9).AddHours(20));
        BacktestHistory grid = new BacktestHistory().High(Monday.AddHours(12)).High(Monday.AddDays(2).AddHours(12)).High(Monday.AddDays(9).AddHours(20));

        PairSetResult single = (await alone.Run(StrategyKind.Idea, alone.Plans(Graphs.H0001Strategy, StrategyKind.Idea, [Thresholds("0.0005")], FourHours))).Single();
        PairSetResult among = (await grid.Run(StrategyKind.Idea, grid.Plans(Graphs.H0001Strategy, StrategyKind.Idea, [Thresholds("0.0003", "0.0005", "0.001")], Hour, FourHours)))[3];

        Assert.That(among.Numbers, Is.EqualTo(single.Numbers).Using<SetNumbers>(Same));
        Assert.That(among.Rows.Select(row => row.Outcome), Is.EqualTo(single.Rows.Select(row => row.Outcome)));
    }

    [Test]
    public async Task One_pair_has_no_random_entries_and_no_difference()
    {
        // «Прогон одной пары».
        BacktestHistory btc = new BacktestHistory().High(Monday.AddDays(7).AddHours(12)).High(Monday.AddDays(9).AddHours(12));

        SetTotals set = (await BacktestHistory.Run(new Dictionary<string, BacktestHistory> { ["BTCUSDT"] = btc }, Plans(btc))).Single();

        Assert.That(set.Numbers.Trades, Is.EqualTo(2));
        Assert.That(set.Cells!.All, Is.Empty);
        Assert.That(set.Numbers.Difference(), Is.Null);
    }

    [Test]
    public async Task A_move_of_the_market_after_the_order_gives_its_pair_and_the_others_alike_and_a_difference_of_zero()
    {
        // «Общее движение рынка»: from the order at 12:00 of the second Monday every pair goes up by 0.01 % a minute from the
        // price it is at, so a short of each is stopped at 2 % alike.
        DateTimeOffset noon = Monday.AddDays(7).AddHours(12);
        Dictionary<string, BacktestHistory> pairs = Market(noon, before: _ => 1);

        SetTotals set = (await BacktestHistory.Run(pairs, Plans(pairs["BTCUSDT"], after: "1d", stop: 0.02m, target: 0.04m))).Single();

        RandomDifference difference = set.Numbers.Difference()!;
        Assert.That(set.Numbers.Trades, Is.EqualTo(1), "the signal is on BTCUSDT alone");
        Assert.That(set.Cells!.Valid(new PairOrder(noon, EntrySide.Short)), Is.EqualTo(3), "ETHUSDT, SOLUSDT and XRPUSDT");
        Assert.That(difference.Weeks, Is.EqualTo(1));
        Assert.That(difference.Mean, Is.EqualTo(0).Within(1e-12));
    }

    [Test]
    public async Task A_fall_of_the_pair_of_the_order_before_its_moment_does_not_move_the_difference()
    {
        // The pair with the signal falls by 10 % from Monday 00:00 to 12:00, the others stand; from the order on all go up alike.
        // The control of engine version 4 drew shorts of the same week on BTCUSDT before the fall: a difference above zero.
        DateTimeOffset noon = Monday.AddDays(7).AddHours(12);
        Dictionary<string, BacktestHistory> pairs = Market(noon, before: time => time < Monday.AddDays(7) ? 1 : 0.9);

        SetTotals set = (await BacktestHistory.Run(pairs, Plans(pairs["BTCUSDT"], after: "1d", stop: 0.02m, target: 0.04m))).Single();

        Assert.That(set.Numbers.Trades, Is.EqualTo(1));
        Assert.That(set.Numbers.Difference()!.Mean, Is.EqualTo(0).Within(1e-12));
    }

    [Test]
    public async Task A_set_alone_and_among_six_over_several_pairs_gives_the_same_numbers_and_random_entries()
    {
        // «Одна группа у всех наборов», «Набор отдельно и в подборе».
        Dictionary<string, BacktestHistory> alone = Signals();
        Dictionary<string, BacktestHistory> grid = Signals();
        List<RandomTrade> single = [];
        List<RandomTrade> among = [];

        SetTotals one = (await BacktestHistory.Run(alone, alone["BTCUSDT"].Plans(Graphs.H0001Strategy, StrategyKind.Idea, [Thresholds("0.0005")], FourHours), (_, results) => single.AddRange(results[0].Randoms))).Single();
        IReadOnlyList<SetTotals> six = await BacktestHistory.Run(
            grid,
            grid["BTCUSDT"].Plans(Graphs.H0001Strategy, StrategyKind.Idea, [Thresholds("0.0003", "0.0005", "0.001")], Hour, FourHours),
            (_, results) => among.AddRange(results[3].Randoms));

        Assert.That(one.Numbers.Difference(), Is.Not.Null, "the set has a difference to compare");
        Assert.That(six[3].Numbers, Is.EqualTo(one.Numbers).Using<SetNumbers>(Same));
        Assert.That(six[3].Pairs.Select(pair => pair.Pair), Is.EqualTo(one.Pairs.Select(pair => pair.Pair)));
        Assert.That(among, Is.EqualTo(single));
        Assert.That(six[3].Cells!.All, Is.EqualTo(one.Cells!.All));
    }

    [Test]
    public async Task A_pair_without_orders_in_the_control_group_reads_the_minutes_of_the_weeks_of_the_orders_of_others_only()
    {
        // «Неделя со случайным входом», «Неделя без событий»: ETHUSDT has no orders; BTCUSDT has one in the second week.
        BacktestHistory btc = new BacktestHistory().High(Monday.AddDays(7).AddHours(12));
        var eth = new BacktestHistory();

        await BacktestHistory.Run(new Dictionary<string, BacktestHistory> { ["BTCUSDT"] = btc, ["ETHUSDT"] = eth }, Plans(btc));

        Assert.That(eth.MinuteReads, Is.EqualTo(new[] { (Monday.AddDays(7), Monday.AddDays(14) + FourHours) }), "the week and the longest time of its steps");
        Assert.That(btc.MinuteReads, Is.EqualTo(new[] { (Monday.AddDays(7), Monday.AddDays(14) + FourHours) }));
    }

    [Test]
    public async Task The_order_of_a_week_before_the_pairs_have_history_has_no_random_entries()
    {
        // The first candles close on Monday 2026-01-05 04:00, after the start of the first week: its group is empty.
        BacktestHistory btc = new BacktestHistory().High(Monday.AddHours(12));
        var eth = new BacktestHistory();

        SetTotals set = (await BacktestHistory.Run(new Dictionary<string, BacktestHistory> { ["BTCUSDT"] = btc, ["ETHUSDT"] = eth }, Plans(btc))).Single();

        Assert.That(set.Numbers.Trades, Is.EqualTo(1));
        Assert.That(set.Cells!.All, Is.Empty);
        Assert.That(eth.MinuteReads, Is.Empty);
    }

    private static ParameterValues Thresholds(params string[] values)
    {
        return new ParameterValues("threshold", values);
    }

    // funding-short with the time written in close-after and, when given, a stop and a target: one set on 4h.
    private static IReadOnlyList<BacktestPlan> Plans(BacktestHistory history, string after = "4h", decimal? stop = null, decimal? target = null)
    {
        string strategy = Graphs.ChangedStrategy(graph =>
        {
            graph.Node("close-after")["inputs"] = new JsonObject { ["after"] = after };
            if (stop is { } distance)
            {
                graph.AddExit("stop-loss", "stop-loss", "distance", JsonValue.Create(distance));
            }

            if (target is { } reach)
            {
                graph.AddExit("take-profit", "take-profit", "distance", JsonValue.Create(reach));
            }
        });
        return history.Plans(strategy, StrategyKind.Idea, [], FourHours);
    }

    // Four pairs at their own levels: the signal on BTCUSDT at noon, a low payment of it two days later; before noon the
    // price of BTCUSDT times «before», after noon every pair goes up by 0.01 % a minute from where it is.
    private static Dictionary<string, BacktestHistory> Market(DateTimeOffset noon, Func<DateTimeOffset, double> before)
    {
        Func<DateTimeOffset, double> Path(double level, Func<DateTimeOffset, double> fall)
        {
            return time => time < noon ? level * fall(time) : level * fall(noon.AddTicks(-1)) * (1 + (0.0001 * (time - noon).TotalMinutes));
        }

        return new Dictionary<string, BacktestHistory>
        {
            ["BTCUSDT"] = new BacktestHistory { Price = Path(100, before) }.High(noon, low: noon.AddDays(2)),
            ["ETHUSDT"] = new BacktestHistory { Price = Path(2000, _ => 1) },
            ["SOLUSDT"] = new BacktestHistory { Price = Path(30, _ => 1) },
            ["XRPUSDT"] = new BacktestHistory { Price = Path(0.5, _ => 1) }
        };
    }

    // Three pairs with signals in the second and the third week, one of them at a moment of another pair.
    private static Dictionary<string, BacktestHistory> Signals()
    {
        return new Dictionary<string, BacktestHistory>
        {
            ["BTCUSDT"] = new BacktestHistory { Price = time => 100 + ((time - Monday).TotalHours % 7) }.High(Monday.AddDays(7).AddHours(12)).High(Monday.AddDays(9).AddHours(12)),
            ["ETHUSDT"] = new BacktestHistory { Price = time => 2000 - ((time - Monday).TotalHours % 5) }.High(Monday.AddDays(7).AddHours(12)).High(Monday.AddDays(15).AddHours(4)),
            ["SOLUSDT"] = new BacktestHistory { Price = time => 30 + ((time - Monday).TotalMinutes % 11 / 10) }.High(Monday.AddDays(8).AddHours(20))
        };
    }

    private static bool Same(SetNumbers a, SetNumbers b)
    {
        return a with { Differences = [], Weeks = [] } == b with { Differences = [], Weeks = [] } && a.Differences.SequenceEqual(b.Differences) && a.Weeks.SequenceEqual(b.Weeks);
    }
}