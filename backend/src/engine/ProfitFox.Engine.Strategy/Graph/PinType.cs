namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     The type of a pin of a node and of the wire that connects to it.
/// </summary>
public enum PinType
{
    /// <summary>
    ///     A decimal number, absent when it is not known yet.
    /// </summary>
    Number,

    /// <summary>
    ///     A logical value, never absent.
    /// </summary>
    Boolean
}