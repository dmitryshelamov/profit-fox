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
        if (side is not (TradeSide.Long or TradeSide.Short))
        {
            throw new ArgumentOutOfRangeException(nameof(side), side, "A trade is long or short.");
        }

        if (horizon < TimeSpan.FromMinutes(1) || horizon.Ticks % TimeSpan.TicksPerMinute != 0 || horizon > TimeSpan.FromDays(365))
        {
            throw new ArgumentOutOfRangeException(nameof(horizon), horizon, "The horizon is a whole number of minutes, at least one.");
        }

        if (stop is not null && !(stop > 0 && stop < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(stop), stop, "The stop is a fraction of the entry price between 0 and 1.");
        }

        if (target is not null && !(target > 0 && target < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "The target is a fraction of the entry price between 0 and 1.");
        }

        Side = side;
        Horizon = horizon;
        Stop = stop;
        Target = target;
    }
}
