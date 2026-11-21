namespace ProfitFox.Engine.Strategy.Execution;

/// <summary>
///     The distance of a stop or a target at one candle, as a fraction of the entry price. A strategy without the node has
///     none; a strategy with the node has a value, which may be missing at the candle (spec strategy-execution «Узлы
///     выхода»).
/// </summary>
public readonly record struct ExitDistance
{
    /// <summary>
    ///     The strategy has no such node.
    /// </summary>
    public static readonly ExitDistance None = default;

    /// <summary>
    ///     Whether the strategy has the node.
    /// </summary>
    public bool InStrategy { get; }

    public decimal? Value { get; }

    private ExitDistance(decimal? value)
    {
        InStrategy = true;
        Value = value;
    }

    /// <summary>
    ///     The distance of a node of the strategy; null when its input is missing at the candle.
    /// </summary>
    public static ExitDistance Of(decimal? value)
    {
        return new ExitDistance(value);
    }
}
