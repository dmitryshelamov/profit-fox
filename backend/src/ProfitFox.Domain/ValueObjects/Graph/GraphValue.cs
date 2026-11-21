using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.ValueObjects.Graph;

/// <summary>
///     A value written in the graph: of a parameter or of an input without a wire. An event has no written value.
/// </summary>
public readonly record struct GraphValue
{
    public PinType Type { get; }

    public decimal Number { get; }

    public bool Boolean { get; }

    public TimeSpan Duration { get; }

    private GraphValue(PinType type, decimal number, bool boolean, TimeSpan duration)
    {
        Type = type;
        Number = number;
        Boolean = boolean;
        Duration = duration;
    }

    public static GraphValue FromNumber(decimal number)
    {
        return new GraphValue(PinType.Number, number, boolean: false, TimeSpan.Zero);
    }

    public static GraphValue FromBoolean(bool boolean)
    {
        return new GraphValue(PinType.Boolean, number: 0, boolean, TimeSpan.Zero);
    }

    public static GraphValue FromDuration(TimeSpan duration)
    {
        return new GraphValue(PinType.Duration, number: 0, boolean: false, duration);
    }
}