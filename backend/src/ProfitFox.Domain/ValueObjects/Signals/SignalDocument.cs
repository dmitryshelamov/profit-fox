using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Domain.ValueObjects.Signals;

/// <summary>
///     A saved signal: its name, an optional description, the graph in the format of spec strategy-graph, the positions of
///     its nodes by id and the number of its latest version (spec signal-constructor «Версии сигнала»).
/// </summary>
public sealed record SignalDocument(
    string Name,
    string? Description,
    GraphDefinition Graph,
    IReadOnlyDictionary<string, NodePosition> Layout,
    int Version = 1);
