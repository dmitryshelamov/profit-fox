namespace ProfitFox.Engine.Simulation.Trades;

/// <summary>
///     How a trade ended; <see cref="None" /> for a trade left out.
/// </summary>
public enum ExitReason
{
    None,
    Stop,
    Target,
    Time
}
