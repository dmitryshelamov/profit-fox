namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <param name="Id"></param>

/// <param name="Type"></param>/// <param name="Inputs">Values set directly on inputs that have no wire.</param>
/// <param name="Settings"></param>
public sealed record GraphNode(string Id, NodeType Type, IReadOnlyDictionary<string, NodeValue> Inputs, NodeSettings Settings);