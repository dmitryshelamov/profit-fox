using ProfitFox.Engine.Strategy.Graph.Model;

namespace ProfitFox.Bench.Signals;

/// <summary>
///     A saved signal: its name, an optional description, the graph in the format of spec strategy-graph and the
///     positions of its nodes by id.
/// </summary>
public sealed record SignalDocument(
    string Name,
    string? Description,
    GraphDefinition Graph,
    IReadOnlyDictionary<string, NodePosition> Layout);
