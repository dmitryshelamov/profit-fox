using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.Backtests;

/// <summary>Requirement "Проверка диапазона".</summary>
public class ParameterRangeTests
{
    private static readonly StrategyParameter Fast = new StrategyParameter("fast", PinType.Number, Values.Number(12m), Min: 2m, Max: 200m, Integer: true);

    [Test]
    public void Range_within_the_limits_is_accepted()
    {
        Range(5m, 15m, 5m).Reject(Fast).ShouldBeNull();
    }

    // Scenario "Шаг не делит диапазон".
    [Test]
    public void Step_that_does_not_divide_the_range_is_rejected()
    {
        Range(5m, 12m, 5m).Reject(Fast).ShouldBe("Step must divide the distance from min to max.");
    }

    // Scenario "Диапазон вне ограничений".
    [Test]
    public void Range_outside_the_limits_is_rejected()
    {
        Range(1m, 10m, 1m).Reject(Fast).ShouldBe("Min must be at least 2.");
        Range(100m, 300m, 100m).Reject(Fast).ShouldBe("Max must be at most 200.");
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Step_that_is_not_positive_is_rejected(decimal step)
    {
        Range(5m, 15m, step).Reject(Fast).ShouldBe("Step must be greater than zero.");
    }

    [Test]
    public void Min_above_max_is_rejected()
    {
        Range(15m, 5m, 5m).Reject(Fast).ShouldBe("Min must not be greater than max.");
    }

    [TestCase(5.5, 0.5)]
    [TestCase(5, 0.5)]
    public void Whole_parameter_needs_a_whole_min_and_step(decimal min, decimal step)
    {
        Range(min, 15m, step).Reject(Fast).ShouldBe("Min and step of a whole parameter must be whole numbers.");
    }

    [Test]
    public void Range_of_another_type_is_rejected()
    {
        var range = new ParameterRange(Values.Duration(TimeSpan.FromHours(1)), Values.Duration(TimeSpan.FromHours(2)), Values.Duration(TimeSpan.FromHours(1)));

        range.Reject(Fast).ShouldBe("Min, max and step must be of type Number.");
    }

    [Test]
    public void Duration_range_is_checked_in_durations()
    {
        var every = new StrategyParameter("every", PinType.Duration, Values.Duration(TimeSpan.FromHours(1)));
        var range = new ParameterRange(Values.Duration(TimeSpan.FromHours(1)), Values.Duration(TimeSpan.FromHours(4)), Values.Duration(TimeSpan.FromHours(2)));

        range.Reject(every).ShouldBe("Step must divide the distance from min to max.");
    }

    private static ParameterRange Range(decimal min, decimal max, decimal step)
    {
        return new ParameterRange(Values.Number(min), Values.Number(max), Values.Number(step));
    }
}