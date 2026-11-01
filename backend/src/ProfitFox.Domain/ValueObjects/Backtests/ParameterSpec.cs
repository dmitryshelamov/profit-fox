using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>What a backtest runs a parameter with: one value or every value of a range.</summary>
public sealed record ParameterSpec
{
    private ParameterSpec(NodeValue? value, ParameterRange? range)
    {
        Value = value;
        Range = range;
    }

    public NodeValue? Value { get; }

    public ParameterRange? Range { get; }

    public long Count => Range?.Count ?? 1;

    public static ParameterSpec Of(NodeValue value)
    {
        return new ParameterSpec(value, null);
    }

    public static ParameterSpec Between(NodeValue min, NodeValue max, NodeValue step)
    {
        return new ParameterSpec(null, new ParameterRange(min, max, step));
    }

    public NodeValue ValueAt(long index)
    {
        return Range?.Value(index) ?? Value!;
    }
}