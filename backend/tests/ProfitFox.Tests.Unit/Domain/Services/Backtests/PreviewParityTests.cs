using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Graph;

using static ProfitFox.Tests.Unit.Domain.Services.Backtests.BacktestHistory;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>
///     Change add-strategy-backtest, task 4.5: the backtest of one set and the preview of the same strategy on one history
///     give the same trades — spec strategy-backtest «Сделки набора» («Как предпросмотр», «Срок за концом истории») and
///     spec strategy-preview «Те же сделки, что у стенда». The price moves every hour, so every trade has a result of its
///     own.
/// </summary>
public sealed class PreviewParityTests
{
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);

    [Test]
    public async Task The_trades_of_a_set_are_those_of_the_preview()
    {
        BacktestHistory history = Moving();
        // A high payment at a close of 4h every three days, at another hour each time, and a low one at the next close.
        for (int day = 0; day < 20; day += 3)
        {
            DateTimeOffset high = Monday.AddDays(day).AddHours(4 * (1 + day % 5));
            history.High(high, low: high.AddHours(4));
        }

        PairSetResult backtest = (await history.Run(StrategyKind.Idea, history.Plans(Graphs.H0001Strategy, StrategyKind.Idea, [], FourHours))).Single();
        IReadOnlyList<OrderOutcome> preview = await Preview(history, TradeCosts.Default);

        TradeOutcome[] previewTrades = [.. preview.Where(order => order.Result == OrderResult.Filled).Select(order => order.Trade!)];
        TradeOutcome[] backtestTrades = [.. backtest.Rows.Where(row => row.Status == TradeStatus.Ok).Select(row => row.Outcome!)];
        Assert.That(previewTrades, Has.Length.GreaterThan(3));
        Assert.That(backtestTrades, Is.EqualTo(previewTrades), "the same moments and prices of entry and exit and the same result");
        Assert.That(backtest.Numbers.Trades, Is.EqualTo(previewTrades.Length));
        Assert.That(backtest.Numbers.NetSum, Is.EqualTo(previewTrades.Sum(trade => trade.Net!.Value)));
    }

    [Test]
    public async Task An_order_whose_time_ends_after_the_end_gives_no_trade_for_the_reason_period()
    {
        // On hourly candles the order comes at 22:00 of the last day; its time of 4h ends two hours after the end.
        BacktestHistory history = new BacktestHistory { End = Monday.AddDays(7) }.High(Monday.AddDays(7).AddHours(-2));

        PairSetResult backtest = (await history.Run(StrategyKind.Idea, history.Plans(Graphs.H0001Strategy, StrategyKind.Idea, [], TimeSpan.FromHours(1)))).Single();
        IReadOnlyList<OrderOutcome> preview = await Preview(history, TradeCosts.Default, TimeSpan.FromHours(1));

        Assert.That(backtest.Rows, Is.Empty);
        Assert.That((backtest.Numbers.Trades, backtest.Numbers.Excluded.Period, backtest.Numbers.Orders), Is.EqualTo((0, 1, 1)));
        Assert.That(preview.Single().Exclusion, Is.EqualTo(OrderExclusion.Period), "the preview tells the same order the same way");
    }

    // A price that goes up and down by the hour.
    private static BacktestHistory Moving()
    {
        return new BacktestHistory { Price = time => 100 + (time.Hour % 5) - (time.Minute % 7) * 0.1 + time.Day * 0.3 };
    }

    // The preview of the strategy on the same history, as the preview of a strategy by plan computes it.
    private static async Task<IReadOnlyList<OrderOutcome>> Preview(BacktestHistory history, TradeCosts costs, TimeSpan? timeframe = null)
    {
        TimeSpan frame = timeframe ?? FourHours;
        BacktestPlan plan = history.Plans(Graphs.H0001Strategy, StrategyKind.Idea, [], frame).Single();
        HistoryRead<HistoryPayment> funding = history.Funding();
        var steps = PairSteps.Of(plan.Strategy, frame, "BTCUSDT", history.Candles(frame)[frame], funding.Items);
        var orders = new PairOrders(steps, Monday, history.End, costs);
        foreach (long week in orders.Weeks)
        {
            (DateTimeOffset from, DateTimeOffset to) = orders.Span(week);
            orders.Evaluate(week, MinutePaths.Build(from, to, await history.Minutes(from, to, CancellationToken.None), funding));
        }

        return orders.Outcomes();
    }
}
