namespace ProfitFox.Engine.Simulation.Orders;

/// <summary>
///     The side of a position: long gains when the price rises, short when it falls.
/// </summary>
public enum PositionSide
{
    /// <summary>
    ///     A long position.
    /// </summary>
    Long,

    /// <summary>
    ///     A short position.
    /// </summary>
    Short
}