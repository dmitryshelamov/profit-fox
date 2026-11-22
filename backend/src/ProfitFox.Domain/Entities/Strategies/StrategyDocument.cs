using ProfitFox.Domain.Entities.Base;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Domain.Entities.Strategies;

/// <summary>
///     A saved strategy (spec strategy-constructor «Сохранённая стратегия»): its name, an optional description, the graph as
///     written — with its <c>signal</c> nodes, not expanded — the positions of its nodes by id and the number of its latest
///     version. The mirror of a saved signal: the repository decides the version when it keeps the strategy.
///     <see cref="Raised" /> says that the graph is not the stored one: the store keeps the form before change
///     add-position-node, and the graph was lifted when it was read (spec strategy-constructor «Подъём не переписывает
///     версии»), so saving it as it is makes a new version.
/// </summary>
public sealed class StrategyDocument : AggregateRoot
{
    public Guid Id { get; }

    public string Name { get; private set; }

    public string? Description { get; }

    public GraphDefinition Graph { get; }

    public IReadOnlyDictionary<string, NodePosition> Layout { get; }

    public int Version { get; }

    public bool Raised { get; }

    private StrategyDocument(Guid id, string name, string? description, GraphDefinition graph, IReadOnlyDictionary<string, NodePosition> layout, int version, bool raised)
    {
        Id = id;
        Name = name;
        Description = description;
        Graph = graph;
        Layout = layout;
        Version = version;
        Raised = raised;
    }

    /// <summary>
    ///     A strategy that is not saved yet: its first version, with an id of its own.
    /// </summary>
    public static StrategyDocument Create(string name, string? description, GraphDefinition graph, IReadOnlyDictionary<string, NodePosition> layout)
    {
        return new StrategyDocument(Guid.NewGuid(), name, description, graph, layout, version: 1, raised: false);
    }

    /// <summary>
    ///     A stored strategy brought back as it was saved, for a repository: nothing happens to it, so it changes no state and
    ///     raises no event. <paramref name="raised" /> is true when <paramref name="graph" /> is the stored graph lifted.
    /// </summary>
    public static StrategyDocument Restore(
        Guid id,
        string name,
        string? description,
        GraphDefinition graph,
        IReadOnlyDictionary<string, NodePosition> layout,
        int version,
        bool raised = false)
    {
        return new StrategyDocument(id, name, description, graph, layout, version, raised);
    }

    /// <summary>
    ///     The strategy takes the name <paramref name="to" /> and keeps everything else.
    /// </summary>
    public void Rename(string to)
    {
        Name = to;
    }
}
