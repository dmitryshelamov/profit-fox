using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Tests.Shared.Builders.Strategies;

/// <summary>Short names for node values in tests.</summary>
public static class Values
{
    public static NodeValue Number(decimal value)
    {
        return new NumberValue(value);
    }

    public static NodeValue Boolean(bool value)
    {
        return new BooleanValue(value);
    }

    public static NodeValue Duration(TimeSpan value)
    {
        return new DurationValue(value);
    }
}