namespace ProfitFox.Domain.ValueObjects.Strategies;

public sealed record Graph(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<Wire> Wires, IReadOnlyDictionary<string, NodePosition> Layout);