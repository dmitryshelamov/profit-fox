namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     A composite node as it is written, before it is checked: its declared outputs, its nodes and its wires.
/// </summary>
public sealed record CompositeNodeDefinition(
    IReadOnlyList<PinDeclaration> Outputs,
    IReadOnlyList<NodeDefinition> Nodes,
    IReadOnlyList<WireDefinition> Wires);