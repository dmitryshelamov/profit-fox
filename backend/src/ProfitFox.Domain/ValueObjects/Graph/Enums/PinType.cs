namespace ProfitFox.Domain.ValueObjects.Graph.Enums;

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
    Event,

    /// <summary>
    ///     What a node of an order gives the node <c>position</c> (change add-position-node, design.md, decision 1): a wire
    ///     that shows where the order goes. It carries no value.
    /// </summary>
    Order,

    /// <summary>
    ///     The link of an exit node to the position it closes: a wire from the output <c>position</c> of the node
    ///     <c>position</c>. It carries no value.
    /// </summary>
    Position
}