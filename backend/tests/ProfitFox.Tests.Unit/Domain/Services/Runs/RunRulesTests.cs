using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

using static ProfitFox.Tests.Unit.Domain.Services.Runs.Designs;

namespace ProfitFox.Tests.Unit.Domain.Services.Runs;

/// <summary>
///     Spec strategy-backtest «Случайные входы» and «Разница со случайным входом»: weeks, the history of an order and the
///     orders that count.
/// </summary>
public sealed class RunRulesTests
{
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);
    private static readonly long Week = Blocks.Of(Utc(2026, 1, 5), TimeBlock.Week);

    [Test]
    public void An_order_whose_trade_crosses_the_end_of_history_is_left_out()
    {
        BacktestDesign design = Exploration();

        Assert.That(design.Fits(Utc(2026, 5, 15, 20), FourHours), Is.True, "ends exactly at the end of history");
        Assert.That(design.Fits(Utc(2026, 5, 15, 22), FourHours), Is.False);
        Assert.That(design.Fits(Utc(2026, 5, 16), FourHours), Is.False);
    }

    [Test]
    public void A_week_starts_on_Monday()
    {
        long week = Blocks.Of(Utc(2026, 1, 7, 13), TimeBlock.Week);

        Assert.That(Blocks.Start(week, TimeBlock.Week), Is.EqualTo(Utc(2026, 1, 5)));
        Assert.That(Utc(2026, 1, 5).DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
        Assert.That(Blocks.Of(Utc(2026, 1, 4, 23), TimeBlock.Week), Is.EqualTo(week - 1));
        Assert.That(Blocks.Of(Utc(2026, 1, 11, 23), TimeBlock.Week), Is.EqualTo(week));
    }

    [Test]
    public void An_order_with_too_few_valid_random_entries_does_not_count()
    {
        var cells = new ControlCells();
        var order = new OrderTrade(Week, Utc(2026, 1, 5, 12), EntrySide.Long, 0.01);
        cells.Add([Random("ETHUSDT", order, 0.002)]);

        Assert.That(cells.Differences([order]), Is.Empty, "one valid random entry");

        cells.Add([Random("SOLUSDT", order, 0.004)]);

        Assert.That(cells.Differences([order]).Single().Difference, Is.EqualTo(0.007).Within(1e-15), "two valid random entries");
    }

    private static RandomTrade Random(string pair, OrderTrade order, double net)
    {
        var outcome = new TradeOutcome(TradeStatus.Ok, ExitReason.Time, Ambiguous: false, order.Moment, order.Moment.AddHours(4), 100, 100, net, 0, 0, 0, net, R: null);
        return new RandomTrade(pair, order.Moment, order.Side, outcome);
    }
}