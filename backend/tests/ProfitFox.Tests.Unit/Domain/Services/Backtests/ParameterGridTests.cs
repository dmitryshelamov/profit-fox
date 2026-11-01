using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

public class ParameterGridTests
{
    // Scenario "Нумерация комбинаций".
    [Test]
    public void Combinations_are_numbered_with_parameters_by_name_and_the_last_one_changing_fastest()
    {
        var grid = new ParameterGrid(new Dictionary<string, ParameterSpec>
        {
            ["slow"] = Range(20m, 30m, 10m),
            ["fast"] = Range(5m, 10m, 5m)
        });

        grid.Count.ShouldBe(4);
        Enumerable.Range(0, 4).Select(index => Pair(grid.Combination(index))).ShouldBe([(5m, 20m), (5m, 30m), (10m, 20m), (10m, 30m)]);
    }

    // Scenario "Полный перебор двух диапазонов".
    [Test]
    public void Three_values_and_four_values_give_twelve_combinations_each_once()
    {
        var grid = new ParameterGrid(new Dictionary<string, ParameterSpec>
        {
            ["fast"] = Range(5m, 15m, 5m),
            ["slow"] = Range(20m, 50m, 10m),
            ["amount"] = ParameterSpec.Of(Values.Number(100m))
        });

        grid.Count.ShouldBe(12);
        Enumerable.Range(0, 12).Select(index => Pair(grid.Combination(index))).Distinct().Count().ShouldBe(12);
        grid.Combination(11)["amount"].ShouldBe(Values.Number(100m));
    }

    // Scenario "Только значения".
    [Test]
    public void Values_only_give_one_combination()
    {
        var grid = new ParameterGrid(new Dictionary<string, ParameterSpec> { ["fast"] = ParameterSpec.Of(Values.Number(12m)) });

        grid.Count.ShouldBe(1);
        grid.Combination(0).ShouldBe(new Dictionary<string, NodeValue> { ["fast"] = Values.Number(12m) });
    }

    [Test]
    public void Duration_range_steps_by_duration()
    {
        var grid = new ParameterGrid(new Dictionary<string, ParameterSpec>
        {
            ["every"] = ParameterSpec.Between(Values.Duration(TimeSpan.FromHours(1)), Values.Duration(TimeSpan.FromHours(3)), Values.Duration(TimeSpan.FromHours(1)))
        });

        Enumerable.Range(0, 3).Select(index => grid.Combination(index)["every"])
            .ShouldBe([Values.Duration(TimeSpan.FromHours(1)), Values.Duration(TimeSpan.FromHours(2)), Values.Duration(TimeSpan.FromHours(3))]);
    }

    [Test]
    public void Count_that_does_not_fit_a_long_is_missing()
    {
        var grid = new ParameterGrid(new Dictionary<string, ParameterSpec>
        {
            ["a"] = Range(1m, 10_000_000m, 1m),
            ["b"] = Range(1m, 10_000_000m, 1m),
            ["c"] = Range(1m, 10_000_000m, 1m)
        });

        grid.Count.ShouldBeNull();
    }

    private static ParameterSpec Range(decimal min, decimal max, decimal step)
    {
        return ParameterSpec.Between(Values.Number(min), Values.Number(max), Values.Number(step));
    }

    private static (decimal Fast, decimal Slow) Pair(IReadOnlyDictionary<string, NodeValue> combination)
    {
        return (((NumberValue)combination["fast"]).Value, ((NumberValue)combination["slow"]).Value);
    }
}