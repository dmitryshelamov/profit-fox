namespace ProfitFox.Engine.Simulation.Trades;

/// <summary>
///     A plan of a trade (spec trade-plan-outcome): side, horizon counted from the moment of the event, and optional stop
///     and target as fractions of the entry price, strictly between 0 and 1.
/// </summary>
public sealed record TradePlan
{
    public TradeSide Side { get; }

    public TimeSpan Horizon { get; }

    public double? Stop { get; }

    public double? Target { get; }

    public int HorizonMinutes => (int)(Horizon.Ticks / TimeSpan.TicksPerMinute);

    public TradePlan(TradeSide side, TimeSpan horizon, double? stop = null, double? target = null)
    {
        if (Problem(side, horizon, stop, target) is { } problem)
        {
            throw new ArgumentOutOfRangeException(problem.Name, problem.Value, problem.Message);
        }

        Side = side;
        Horizon = horizon;
        Stop = stop;
        Target = target;
    }

    /// <summary>
    ///     The plan, or null when the constructor would refuse the values: the same checks without an exception, for values
    ///     that come from a graph at each candle.
    /// </summary>
    public static TradePlan? OrNull(TradeSide side, TimeSpan horizon, double? stop, double? target)
    {
        return Problem(side, horizon, stop, target) is null ? new TradePlan(side, horizon, stop, target) : null;
    }

    // The first value a plan cannot have, by the name of its parameter, or null when the plan is allowed.
    private static (string Name, object? Value, string Message)? Problem(TradeSide side, TimeSpan horizon, double? stop, double? target)
    {
        if (side is not (TradeSide.Long or TradeSide.Short))
        {
            return (nameof(side), side, "A trade is long or short.");
        }

        if (horizon < TimeSpan.FromMinutes(1) || horizon.Ticks % TimeSpan.TicksPerMinute != 0 || horizon > TimeSpan.FromDays(365))
        {
            return (nameof(horizon), horizon, "The horizon is a whole number of minutes, at least one.");
        }

        if (stop is not null && !(stop > 0 && stop < 1))
        {
            return (nameof(stop), stop, "The stop is a fraction of the entry price between 0 and 1.");
        }

        if (target is not null && !(target > 0 && target < 1))
        {
            return (nameof(target), target, "The target is a fraction of the entry price between 0 and 1.");
        }

        return null;
    }
}
