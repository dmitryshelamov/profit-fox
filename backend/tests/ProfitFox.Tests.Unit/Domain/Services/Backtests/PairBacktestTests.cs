using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Hypotheses.Enums;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Graph;

using static ProfitFox.Tests.Unit.Domain.Services.Backtests.BacktestHistory;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>
///     Change add-strategy-backtest, task 4.3: a category of ideas on one pair in one pass for all sets — spec
///     strategy-backtest «Случайные входы», «Заявка при открытой позиции в прогоне», «Повторяемость прогона» and spec
///     market-history «Минутные свечи в окнах сделок». The strategy is funding-short: an order at the close of the candle
///     that first sees a payment at or above the threshold.
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
    public async Task The_moments_of_the_orders_are_not_random_entries()
    {
        // Orders at the closes of 12:00 and 20:00 on Monday.
        BacktestHistory history = new BacktestHistory().High(Monday.AddHours(12), low: Monday.AddHours(13)).High(Monday.AddHours(20));

        PairSetResult result = (await history.Run(StrategyKind.Idea, Plans(history, randoms: 200))).Single();

        Assert.That(result.Rows.Select(row => row.Moment), Is.EqualTo(new[] { Monday.AddHours(12), Monday.AddHours(20) }));
        Assert.That(result.Rows.SelectMany(row => row.Randoms).Select(random => random.Entry), Has.None.EqualTo(Monday.AddHours(12)).And.None.EqualTo(Monday.AddHours(20)));
    }

    [Test]
    public async Task A_random_entry_while_the_cycle_of_an_order_is_open_is_counted_with_the_position_closed()
    {
        BacktestHistory history = new BacktestHistory().High(Monday.AddHours(12));

        EventRow row = (await history.Run(StrategyKind.Idea, Plans(history, after: "1d", randoms: 200))).Single().Rows.Single();

        RandomTrade random = row.Randoms.First(trade => trade.Entry == Monday.AddHours(16));
        Assert.That((row.Outcome!.EntryTime, row.Outcome.ExitTime), Is.EqualTo(((DateTimeOffset?)Monday.AddHours(12), (DateTimeOffset?)Monday.AddDays(1).AddHours(12))));
        Assert.That(random.Outcome.Status, Is.EqualTo(TradeStatus.Ok));
        Assert.That((random.Outcome.EntryTime, random.Outcome.ExitTime), Is.EqualTo(((DateTimeOffset?)Monday.AddHours(16), (DateTimeOffset?)Monday.AddDays(1).AddHours(16))));
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
        Assert.That(among.Rows.SelectMany(row => row.Randoms.Select(random => (random.Entry, random.Outcome))), Is.EqualTo(single.Rows.SelectMany(row => row.Randoms.Select(random => (random.Entry, random.Outcome)))));
        Assert.That(among.Rows.Select(row => row.Outcome), Is.EqualTo(single.Rows.Select(row => row.Outcome)));
    }

    private static ParameterValues Thresholds(params string[] values)
    {
        return new ParameterValues("threshold", values);
    }

    // funding-short with the time written in close-after, one set on 4h with the given number of random entries.
    private static IReadOnlyList<BacktestPlan> Plans(BacktestHistory history, string after = "4h", int randoms = 20)
    {
        string strategy = Graphs.ChangedStrategy(graph => graph.Node("close-after")["inputs"] = new JsonObject { ["after"] = after });
        return [.. history.Plans(strategy, StrategyKind.Idea, [], FourHours).Select(plan => plan with { Design = plan.Design with { Randoms = randoms } })];
    }

    private static bool Same(SetNumbers a, SetNumbers b)
    {
        return a with { Differences = [] } == b with { Differences = [] } && a.Differences.SequenceEqual(b.Differences);
    }
}
