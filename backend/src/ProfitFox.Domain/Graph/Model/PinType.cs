namespace ProfitFox.Domain.Graph.Model;

/// <summary>
///     The type of a pin: what a wire carries between two nodes.
/// </summary>
public enum PinType
{
    Number,

    Boolean,

    Duration,

    /// <summary>
    ///     True only at the moment it happens, as opposed to a boolean condition that holds for as long as it holds.
    /// </summary>
    Event
}