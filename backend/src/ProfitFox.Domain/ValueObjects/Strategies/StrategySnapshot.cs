using ProfitFox.Domain.Services.Strategies.Validation;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>
///     What a strategy executes: its definition and every composite node it uses, directly or through other nodes, built-in
///     ones included. Layout never affects execution, so it is dropped; two snapshots are equal when their content is.
/// </summary>
public sealed record StrategySnapshot
{
    private StrategySnapshot(StrategyDefinition definition, IReadOnlyList<SnapshotComposite> composites)
    {
        Definition = definition;
        Composites = composites;
    }

    public StrategyDefinition Definition { get; }

    /// <summary>Built-in nodes first, then custom ones, each ordered by id.</summary>
    public IReadOnlyList<SnapshotComposite> Composites { get; }

    /// <summary>The snapshot of a valid strategy whose composite nodes are all in <paramref name="catalog" />.</summary>
    public static StrategySnapshot Of(StrategyDefinition definition, CompositeCatalog catalog)
    {
        var found = new Dictionary<CompositeNodeId, CompositeDefinition>();
        Collect(definition.Graph, catalog, found);
        IReadOnlyList<SnapshotComposite> composites =
        [
            .. found.OrderBy(entry => entry.Key, Comparer<CompositeNodeId>.Create(Compare))
                .Select(entry => new SnapshotComposite(entry.Key, entry.Value with { Graph = WithoutLayout(entry.Value.Graph) }))
        ];

        return new StrategySnapshot(definition with { Graph = WithoutLayout(definition.Graph) }, composites);
    }

    /// <summary>Only the nodes of the snapshot, so the version executes the same after any later edit of a node.</summary>
    public CompositeCatalog Catalog()
    {
        return new CompositeCatalog(Composites.ToDictionary(composite => composite.Id, composite => composite.Definition));
    }

    public bool Equals(StrategySnapshot? other)
    {
        return other is not null
            && Definition.Direction == other.Definition.Direction
            && Definition.Parameters.SequenceEqual(other.Definition.Parameters)
            && SameGraph(Definition.Graph, other.Definition.Graph)
            && Composites.Count == other.Composites.Count
            && Composites.Zip(other.Composites).All(pair => pair.First.Id == pair.Second.Id && SameComposite(pair.First.Definition, pair.Second.Definition));
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Definition.Direction, Definition.Parameters.Count, Definition.Graph.Nodes.Count, Composites.Count);
    }

    private static void Collect(Graph graph, CompositeCatalog catalog, Dictionary<CompositeNodeId, CompositeDefinition> found)
    {
        foreach (GraphNode node in graph.Nodes)
        {
            if (node.Type is CompositeNodeType { Id: var id } && !found.ContainsKey(id))
            {
                CompositeDefinition definition = catalog.Find(id)!;
                found[id] = definition;
                Collect(definition.Graph, catalog, found);
            }
        }
    }

    private static int Compare(CompositeNodeId left, CompositeNodeId right)
    {
        return (left, right) switch
        {
            (BuiltInNodeId a, BuiltInNodeId b) => a.Node.CompareTo(b.Node),
            (CustomNodeId a, CustomNodeId b) => a.Value.CompareTo(b.Value),
            (BuiltInNodeId, _) => -1,
            _ => 1
        };
    }

    private static Graph WithoutLayout(Graph graph)
    {
        return graph with { Layout = new Dictionary<string, NodePosition>() };
    }

    private static bool SameComposite(CompositeDefinition left, CompositeDefinition right)
    {
        return left.Inputs.SequenceEqual(right.Inputs) && left.Outputs.SequenceEqual(right.Outputs) && SameGraph(left.Graph, right.Graph);
    }

    private static bool SameGraph(Graph left, Graph right)
    {
        return left.Wires.SequenceEqual(right.Wires)
            && left.Nodes.Count == right.Nodes.Count
            && left.Nodes.Zip(right.Nodes).All(pair => SameNode(pair.First, pair.Second));
    }

    private static bool SameNode(GraphNode left, GraphNode right)
    {
        return left.Id == right.Id
            && left.Type == right.Type
            && left.Settings == right.Settings
            && left.Inputs.Count == right.Inputs.Count
            && left.Inputs.All(input => right.Inputs.TryGetValue(input.Key, out NodeValue? value) && value == input.Value);
    }
}