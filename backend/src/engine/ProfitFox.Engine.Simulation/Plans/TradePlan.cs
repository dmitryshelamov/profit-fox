using ProfitFox.Engine.Simulation.Orders;

namespace ProfitFox.Engine.Simulation.Plans;

/// <summary>
///     The plan of a trade of a hypothesis: its side and how long it is held. Stop and target come with the first hypothesis that has
///     them.
/// </summary>
public sealed record TradePlan
{
    /// <summary>
    ///     Long or short.
    /// </summary>
    public PositionSide Side { get; }

    /// <summary>
    ///     How long the trade is held: a whole number of minutes, at least one.
    /// </summary>
    public TimeSpan Horizon { get; }

    /// <exception cref="ArgumentOutOfRangeException">The horizon is not a whole number of minutes, at least one.</exception>
    public TradePlan(PositionSide side, TimeSpan horizon)
    {
        if (horizon < TimeSpan.FromMinutes(1) || horizon.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(horizon), horizon, "The horizon is a whole number of minutes, at least one.");
        }

        Side = side;
        Horizon = horizon;
    }
}