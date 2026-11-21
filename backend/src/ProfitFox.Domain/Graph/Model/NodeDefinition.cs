namespace ProfitFox.Domain.Graph.Model;

/// <summary>
///     A node: its id in the graph, its kind in the catalog, its settings and the values of inputs without a wire.
/// </summary>
public sealed record NodeDefinition(
    string Id,
    string Kind,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, GraphValue> Inputs);