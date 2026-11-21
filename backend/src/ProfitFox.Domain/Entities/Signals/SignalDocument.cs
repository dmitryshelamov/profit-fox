using ProfitFox.Domain.Entities.Base;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Domain.Entities.Signals;

/// <summary>
///     A saved signal: its name, an optional description, the graph in the format of spec strategy-graph, the positions of
///     its nodes by id and the number of its latest version (spec signal-constructor «Версии сигнала»). The repository decides
///     the version when it keeps the signal; the API knows a signal by its name.
/// </summary>
public sealed class SignalDocument : AggregateRoot
{
    public string Name { get; private set; }

    public string? Description { get; }

    public GraphDefinition Graph { get; }

    public IReadOnlyDictionary<string, NodePosition> Layout { get; }

    public int Version { get; }

    private SignalDocument(string name, string? description, GraphDefinition graph, IReadOnlyDictionary<string, NodePosition> layout, int version)
    {
        Name = name;
        Description = description;
        Graph = graph;
        Layout = layout;
        Version = version;
    }

    /// <summary>
    ///     A signal that is not saved yet: its first version.
    /// </summary>
    public static SignalDocument Create(string name, string? description, GraphDefinition graph, IReadOnlyDictionary<string, NodePosition> layout)
    {
        return new SignalDocument(name, description, graph, layout, version: 1);
    }

    /// <summary>
    ///     A stored signal brought back as it was saved, for a repository: nothing happens to it, so it changes no state and raises no
    ///     event.
    /// </summary>
    public static SignalDocument Restore(string name, string? description, GraphDefinition graph, IReadOnlyDictionary<string, NodePosition> layout, int version)
    {
        return new SignalDocument(name, description, graph, layout, version);
    }

    /// <summary>
    ///     The signal takes the name <paramref name="to" /> and keeps everything else.
    /// </summary>
    public void Rename(string to)
    {
        Name = to;
    }
}
