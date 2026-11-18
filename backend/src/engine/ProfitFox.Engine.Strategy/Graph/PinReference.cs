namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     An end of a wire: a pin of a node of the graph, written as <c>node.pin</c>.
/// </summary>
public sealed record PinReference(string Node, string Pin)
{
    /// <summary>
    ///     The reference as it is written in the definition.
    /// </summary>
    public override string ToString()
    {
        return $"{Node}.{Pin}";
    }
}