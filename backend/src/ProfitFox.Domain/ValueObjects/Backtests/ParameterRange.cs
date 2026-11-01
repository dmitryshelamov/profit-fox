using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>Values from <see cref="Min" /> to <see cref="Max" /> by <see cref="Step" />, both ends included; numbers or durations.</summary>
public sealed record ParameterRange(NodeValue Min, NodeValue Max, NodeValue Step)
{
    public long Count => (long)((Magnitude(Max) - Magnitude(Min)) / Magnitude(Step)) + 1;

    /// <summary>The value number <paramref name="index" />, ascending from <see cref="Min" />.</summary>
    public NodeValue Value(long index)
    {
        decimal value = Magnitude(Min) + index * Magnitude(Step);

        return Min.Type == PinType.Number ? new NumberValue(value) : new DurationValue(TimeSpan.FromTicks((long)value));
    }

    /// <summary>Why the range cannot give values of <paramref name="parameter" />, as a sentence; <c>null</c> when it can.</summary>
    public string? Reject(StrategyParameter parameter)
    {
        if (Min.Type != parameter.Type || Max.Type != parameter.Type || Step.Type != parameter.Type)
        {
            return $"Min, max and step must be of type {parameter.Type}.";
        }

        decimal min = Magnitude(Min);
        decimal max = Magnitude(Max);
        decimal step = Magnitude(Step);

        if (step <= 0m)
        {
            return "Step must be greater than zero.";
        }

        if (min > max)
        {
            return "Min must not be greater than max.";
        }

        if ((max - min) % step != 0m)
        {
            return "Step must divide the distance from min to max.";
        }

        if (parameter.Integer && (min != decimal.Truncate(min) || step != decimal.Truncate(step)))
        {
            return "Min and step of a whole parameter must be whole numbers.";
        }

        return parameter.Reject(Min) is { } low ? $"Min {low}" : parameter.Reject(Max) is { } high ? $"Max {high}" : null;
    }

    /// <summary>A number as it is, a duration in ticks.</summary>
    private static decimal Magnitude(NodeValue value)
    {
        return value is NumberValue number ? number.Value : ((DurationValue)value).Value.Ticks;
    }
}