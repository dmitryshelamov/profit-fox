namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     A checked composite node, ready to execute: its declared outputs, its nodes in an order where every node comes after the nodes
///     that feed it, and its wires. Only <see cref="CompositeNodeReader" /> creates it.
/// </summary>
public sealed class CompositeNode
{
    /// <summary>
    ///     The declared outputs.
    /// </summary>
    public IReadOnlyList<PinDeclaration> Outputs { get; }

    /// <summary>
    ///     The nodes, each after the nodes whose outputs are wired to it.
    /// </summary>
    public IReadOnlyList<NodeDefinition> Nodes { get; }

    /// <summary>
    ///     The wires.
    /// </summary>
    public IReadOnlyList<WireDefinition> Wires { get; }

    internal CompositeNode(IReadOnlyList<PinDeclaration> outputs, IReadOnlyList<NodeDefinition> nodes, IReadOnlyList<WireDefinition> wires)
    {
        Outputs = outputs;
        Nodes = nodes;
        Wires = wires;
    }
}