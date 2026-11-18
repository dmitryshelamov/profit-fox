namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     A value on a pin: a number, possibly absent, or a logical value.
/// </summary>
public readonly record struct PinValue
{
    /// <summary>
    ///     The type of the value.
    /// </summary>
    public PinType Type { get; }

    /// <summary>
    ///     The number of a <see cref="PinType.Number" /> value; null when the number is absent.
    /// </summary>
    public decimal? Number { get; }

    /// <summary>
    ///     The logical value of a <see cref="PinType.Boolean" /> value.
    /// </summary>
    public bool Boolean { get; }

    private PinValue(PinType type, decimal? number, bool boolean)
    {
        Type = type;
        Number = number;
        Boolean = boolean;
    }

    /// <summary>
    ///     A number; null when it is absent.
    /// </summary>
    public static PinValue FromNumber(decimal? number)
    {
        return new PinValue(PinType.Number, number, boolean: false);
    }

    /// <summary>
    ///     A logical value.
    /// </summary>
    public static PinValue FromBoolean(bool boolean)
    {
        return new PinValue(PinType.Boolean, number: null, boolean);
    }
}