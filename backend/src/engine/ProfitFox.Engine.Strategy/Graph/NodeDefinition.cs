namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     A node of a graph as it is written: its id, its kind from the library, its settings and the values of its inputs that no wire
///     feeds.
/// </summary>
public sealed record NodeDefinition(
    string Id,
    string Kind,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, PinValue> Inputs);