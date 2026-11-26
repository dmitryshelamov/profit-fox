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
///     Spec strategy-backtest «Разница со случайным входом» (change fix-backtest-random-control, design.md, decision 5, task
///     3.3): the cells of control of a set keep the sums of the valid random entries of each moment and side, and the
///     difference of an order is its result minus the mean of its cell.
/// </summary>
public sealed class ControlCellsTests
{
    private static readonly DateTimeOffset Noon = Utc(2026, 1, 5, 12);
    private static readonly long Week = Blocks.Of(Noon, TimeBlock.Week);

    [Test]
    public void The_difference_of_an_order_is_its_result_minus_the_mean_of_its_valid_random_entries()
    {
        var cells = new ControlCells();
        cells.Add([Random("ETHUSDT", Noon, 0.01), Random("SOLUSDT", Noon, -0.03), Left("XRPUSDT", Noon)]);

        WeekDifference difference = cells.Differences([Order(Noon, 0.02)]).Single();

        Assert.That(difference.Difference, Is.EqualTo(0.03).Within(1e-15), "+0.02 minus the mean of +0.01 and -0.03");
        Assert.That(difference.Week, Is.EqualTo(Week));
        Assert.That(cells.Valid(new PairOrder(Noon, EntrySide.Long)), Is.EqualTo(2), "the entry left out is not valid");
    }

    [Test]
    public void Orders_of_two_pairs_in_one_week_average_into_its_mean()
    {
        // «Две заявки в одной неделе»: the differences +0.4 % and -0.2 % give the week +0.1 %.
        DateTimeOffset four = Utc(2026, 1, 5, 16);
        var cells = new ControlCells();
        cells.Add([Random("ETHUSDT", Noon, 0.001), Random("SOLUSDT", Noon, 0.001), Random("ETHUSDT", four, 0.003), Random("SOLUSDT", four, 0.003)]);
        SetNumbers btc = SetNumbers.Zero with { Differences = cells.Differences([Order(Noon, 0.005)]) };
        SetNumbers ada = SetNumbers.Zero with { Differences = cells.Differences([Order(four, 0.001)]) };

        (long week, double mean, int orders) = (btc + ada).WeekMeans().Single();

        Assert.That((week, orders), Is.EqualTo((Week, 2)));
        Assert.That(mean, Is.EqualTo(0.001).Within(1e-15));
    }

    [Test]
    public void An_order_with_one_valid_random_entry_does_not_count()
    {
        // «Случайные входы вне покрытия».
        var cells = new ControlCells();
        cells.Add([Random("ETHUSDT", Noon, 0.01), Left("SOLUSDT", Noon), Left("XRPUSDT", Noon)]);

        Assert.That(cells.Differences([Order(Noon, 0.02)]), Is.Empty);
        Assert.That((SetNumbers.Zero with { Differences = cells.Differences([Order(Noon, 0.02)]) }).Difference(), Is.Null);
    }

    [Test]
    public void Orders_of_one_week_give_a_difference_without_t()
    {
        // «Одна неделя».
        DateTimeOffset four = Utc(2026, 1, 5, 16);
        var cells = new ControlCells();
        cells.Add([Random("ETHUSDT", Noon, 0), Random("SOLUSDT", Noon, 0), Random("ETHUSDT", four, 0), Random("SOLUSDT", four, 0)]);

        RandomDifference difference = (SetNumbers.Zero with { Differences = cells.Differences([Order(Noon, 0.004), Order(four, 0.002)]) }).Difference()!;

        Assert.That((difference.Weeks, difference.T), Is.EqualTo((1, (double?)null)));
        Assert.That(difference.Mean, Is.EqualTo(0.003).Within(1e-15));
    }

    [Test]
    public void The_cells_of_another_moment_or_side_are_other_cells()
    {
        var cells = new ControlCells();
        cells.Add([Random("ETHUSDT", Noon, 0.01), Random("SOLUSDT", Noon, 0.01)]);
        cells.Add([new RandomTrade("ETHUSDT", Noon, EntrySide.Short, Outcome(Noon, -0.05)), new RandomTrade("SOLUSDT", Noon, EntrySide.Short, Outcome(Noon, -0.05))]);

        Assert.That(cells.Mean(new PairOrder(Noon, EntrySide.Long))!.Y, Is.EqualTo(0.01).Within(1e-15));
        Assert.That(cells.Mean(new PairOrder(Noon, EntrySide.Short))!.Y, Is.EqualTo(-0.05).Within(1e-15));
        Assert.That(cells.Mean(new PairOrder(Noon.AddHours(4), EntrySide.Long)), Is.Null);
        Assert.That(cells.All.Select(cell => (cell.Order.Side, cell.Valid)), Is.EqualTo(new[] { (EntrySide.Long, 2), (EntrySide.Short, 2) }));
    }

    private static OrderTrade Order(DateTimeOffset moment, double y)
    {
        return new OrderTrade(Blocks.Of(moment, TimeBlock.Week), moment, EntrySide.Long, y);
    }

    private static RandomTrade Random(string pair, DateTimeOffset moment, double net)
    {
        return new RandomTrade(pair, moment, EntrySide.Long, Outcome(moment, net));
    }

    private static RandomTrade Left(string pair, DateTimeOffset moment)
    {
        return new RandomTrade(pair, moment, EntrySide.Long, TradeOutcome.Excluded(TradeStatus.Coverage));
    }

    private static TradeOutcome Outcome(DateTimeOffset moment, double net)
    {
        return new TradeOutcome(TradeStatus.Ok, ExitReason.Time, Ambiguous: false, moment, moment.AddHours(4), 100, 100, net, 0, 0, 0, net, R: null);
    }
}