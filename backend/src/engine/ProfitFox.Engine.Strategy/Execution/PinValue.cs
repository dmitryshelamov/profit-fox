using ProfitFox.Engine.Strategy.Graph.Model;

namespace ProfitFox.Engine.Strategy.Execution;

/// <summary>
///     The value on a wire at one candle. A number, a boolean or a duration may be missing (spec strategy-execution); an
///     event either happened or did not.
/// </summary>
public readonly record struct PinValue
{
    public PinType Type { get; }

    public decimal? Number { get; }

    /// <summary>
    ///     The value of a boolean, or whether an event happened.
    /// </summary>
    public bool? Boolean { get; }

    public TimeSpan? Duration { get; }

    public bool IsMissing => Type switch
    {
        PinType.Number => Number is null,
        PinType.Boolean => Boolean is null,
        PinType.Duration => Duration is null,
        _ => false
    };

    public bool Happened => Type == PinType.Event && Boolean == true;

    private PinValue(PinType type, decimal? number, bool? boolean, TimeSpan? duration)
    {
        Type = type;
        Number = number;
        Boolean = boolean;
        Duration = duration;
    }

    public static PinValue FromNumber(decimal? number)
    {
        return new PinValue(PinType.Number, number, boolean: null, duration: null);
    }

    public static PinValue FromBoolean(bool? boolean)
    {
        return new PinValue(PinType.Boolean, number: null, boolean, duration: null);
    }

    public static PinValue FromDuration(TimeSpan? duration)
    {
        return new PinValue(PinType.Duration, number: null, boolean: null, duration);
    }

    public static PinValue FromEvent(bool happened)
    {
        return new PinValue(PinType.Event, number: null, happened, duration: null);
    }

    public static PinValue Missing(PinType type)
    {
        return type == PinType.Event ? FromEvent(happened: false) : new PinValue(type, number: null, boolean: null, duration: null);
    }

    public static PinValue From(GraphValue value)
    {
        return value.Type switch
        {
            PinType.Number => FromNumber(value.Number),
            PinType.Boolean => FromBoolean(value.Boolean),
            PinType.Duration => FromDuration(value.Duration),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Type, "An event has no written value.")
        };
    }
}
