using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Graph;

using static ProfitFox.Tests.Unit.Domain.Services.Backtests.BacktestHistory;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>
///     Change add-strategy-backtest, task 4.4: a strategy for a bot on one pair — the minutes of the whole history in one
///     path, a position per set, a cycle open at the end of history (spec strategy-backtest «Сделки набора», «Лесенка у
///     конца истории»; «Результат набора», «Незакрытый цикл»; «Случайные входы», «Лесенка без случайных входов»). The
///     ladder of <c>Fixtures/ladder.json</c> on hourly candles of one day.
/// </summary>
public sealed class BotBacktestTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Test]
    public async Task The_ladder_at_the_end_of_history_has_its_last_cycle_open()
    {
        // At 100 the ladder buys on the first close; at 23:58 the price is 102 and the limit sale above the average fills.
        // The market order buys again in the next minute, the last of history: that cycle is open at the end.
        BacktestHistory history = Day();

        PairSetResult result = (await history.Run(StrategyKind.Bot, history.Plans(Graphs.Ladder, StrategyKind.Bot, [], Hour))).Single();

        PositionResult cycles = result.Cycles!;
        CycleOutcome open = cycles.Cycles[^1];
        Assert.That(cycles.Cycles.Count(cycle => !cycle.Open), Is.EqualTo(1));
        Assert.That(open.Open, Is.True);
        Assert.That(open.Fills.Single().Moment, Is.EqualTo(Monday.AddDays(1).AddMinutes(-1)), "the position opened in the last minute of history");
        Assert.That(result.Numbers.OpenCycles, Is.EqualTo(1));
        Assert.That(result.Numbers.OpenAmount, Is.EqualTo(open.NetAmount));
        Assert.That(result.Numbers.Trades, Is.EqualTo(1));
        Assert.That(result.Numbers.Amount, Is.EqualTo(cycles.Cycles[0].NetAmount), "the estimate of the open cycle is not in the sum");
    }

    [Test]
    public async Task A_strategy_for_a_bot_reads_the_minutes_of_its_whole_history_once_for_every_set()
    {
        BacktestHistory history = Day();

        IReadOnlyList<PairSetResult> results = await history.Run(
            StrategyKind.Bot,
            history.Plans(Graphs.Ladder, StrategyKind.Bot, [new ParameterValues("priceRiseForSell", ["0.01", "0.05"])], Hour));

        Assert.That(history.MinuteReads, Is.EqualTo(new[] { (Monday, Monday.AddDays(1)) }));
        Assert.That(results.Select(result => result.Numbers.Trades), Is.EqualTo(new[] { 1, 0 }), "a rise of 5 % never comes");
    }

    [Test]
    public async Task The_ladder_has_no_random_entries()
    {
        BacktestHistory history = Day();

        PairSetResult result = (await history.Run(StrategyKind.Bot, history.Plans(Graphs.Ladder, StrategyKind.Bot, [], Hour))).Single();

        Assert.That(result.Rows, Is.Empty);
        Assert.That(result.Numbers.Differences, Is.Empty);
        Assert.That(result.Numbers.Difference(), Is.Null);
    }

    /// <summary>
    ///     Task 12.3: a cycle that a hole in the minutes breaks is neither a trade nor open; it is without a trade on
    ///     coverage, as an order of a category of ideas whose minutes have a hole.
    /// </summary>
    [Test]
    public async Task A_cycle_broken_by_a_hole_after_the_entry_is_without_a_trade_on_coverage()
    {
        // The ladder buys at 01:00; the hole at 06:00 breaks that cycle.
        var history = new BacktestHistory
        {
            End = Monday.AddDays(1),
            Price = time => time >= Monday.AddDays(1).AddMinutes(-2) ? 102 : 100,
            Hole = (Monday.AddHours(6), Monday.AddHours(7))
        };

        PairSetResult result = (await history.Run(StrategyKind.Bot, history.Plans(Graphs.Ladder, StrategyKind.Bot, [], Hour))).Single();

        IReadOnlyList<CycleOutcome> cycles = result.Cycles!.Cycles;
        Assert.That(cycles.Count(cycle => cycle.Status == TradeStatus.Coverage), Is.EqualTo(1));
        int ordersOnCoverage = result.Cycles.Orders.Count(order => order.Exclusion == OrderExclusion.Coverage);
        Assert.That(result.Numbers.Excluded.Coverage, Is.EqualTo(1 + ordersOnCoverage), "the broken cycle by its reason");
        Assert.That(result.Numbers.Trades + result.Numbers.OpenCycles + 1, Is.EqualTo(cycles.Count), "every cycle is counted once");
    }

    private static BacktestHistory Day()
    {
        return new BacktestHistory { End = Monday.AddDays(1), Price = time => time >= Monday.AddDays(1).AddMinutes(-2) ? 102 : 100 };
    }
}
