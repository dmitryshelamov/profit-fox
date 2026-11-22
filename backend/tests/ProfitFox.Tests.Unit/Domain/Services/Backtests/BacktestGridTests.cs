using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>
///     Change add-strategy-backtest, task 3.2: spec strategy-backtest «Наборы значений» — the sets in the order of the
///     parameters of the graph, then of the timeframes, numbered from 1.
/// </summary>
public sealed class BacktestGridTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);

    [Test]
    public void Three_thresholds_and_two_timeframes_are_six_sets()
    {
        IReadOnlyList<BacktestSet> sets = BacktestGrid.Of([new ParameterValues("threshold", ["0.0003", "0.0005", "0.001"])], [Hour, FourHours]);

        Assert.That(sets.Select(set => (set.Number, set.Values.Single().Value, set.Timeframe)), Is.EqualTo(new[]
        {
            (1, "0.0003", Hour), (2, "0.0003", FourHours), (3, "0.0005", Hour), (4, "0.0005", FourHours), (5, "0.001", Hour), (6, "0.001", FourHours)
        }));
        Assert.That(BacktestGrid.Count([new ParameterValues("threshold", ["0.0003", "0.0005", "0.001"])], 2), Is.EqualTo(6));
    }

    [Test]
    public void The_first_parameter_of_the_graph_changes_slowest()
    {
        IReadOnlyList<BacktestSet> sets = BacktestGrid.Of(
            [new ParameterValues("priceRiseForSell", ["0.01", "0.02"]), new ParameterValues("limitDeposit", ["1000", "2000"])],
            [FourHours]);

        Assert.That(
            sets.Select(set => string.Join(" ", set.Values.Select(value => $"{value.Key}={value.Value}"))),
            Is.EqualTo(new[]
            {
                "priceRiseForSell=0.01 limitDeposit=1000", "priceRiseForSell=0.01 limitDeposit=2000",
                "priceRiseForSell=0.02 limitDeposit=1000", "priceRiseForSell=0.02 limitDeposit=2000"
            }));
    }

    [Test]
    public void A_parameter_without_values_is_not_in_the_grid()
    {
        IReadOnlyList<BacktestSet> sets = BacktestGrid.Of([new ParameterValues("threshold", ["0.0005"]), new ParameterValues("after", [])], [FourHours]);

        BacktestSet set = sets.Single();
        Assert.That(set.Values.Select(value => value.Key), Is.EqualTo(new[] { "threshold" }), "the time keeps the value of the graph");
        Assert.That(BacktestGrid.Of([], [FourHours]).Single().Values, Is.Empty);
    }

    [Test]
    public void The_count_stops_past_the_limit()
    {
        var many = new ParameterValues("threshold", [.. Enumerable.Range(1, 1000).Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))]);

        Assert.That(BacktestGrid.Count([many, many, many], 3), Is.EqualTo(201), "the grid is not made to be counted");
    }
}
