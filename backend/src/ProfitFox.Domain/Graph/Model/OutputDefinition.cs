namespace ProfitFox.Domain.Graph.Model;

/// <summary>
///     A declared output of the graph, taken by the <c>outputs</c> node.
/// </summary>
public sealed record OutputDefinition(string Name, PinType Type);