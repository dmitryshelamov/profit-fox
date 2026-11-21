namespace ProfitFox.Domain.Graph.Model;

/// <summary>
///     A graph as written: what the JSON says, before the meaning of its nodes and wires is checked.
/// </summary>
public sealed record GraphDefinition(
    IReadOnlyList<ParameterDefinition> Parameters,
    IReadOnlyList<OutputDefinition> Outputs,
    IReadOnlyList<NodeDefinition> Nodes,
    IReadOnlyList<WireDefinition> Wires)
{
    public const int FormatVersion = 1;
}