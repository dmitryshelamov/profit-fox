using ProfitFox.Domain.Entities.Base;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Entities.Strategies;

/// <summary>
///     Strategies and custom composite nodes of the owner. Every operation keeps the invariant that each saved graph is
///     valid, because an edit of a composite node reaches every graph that uses it.
/// </summary>
public sealed class StrategyLibrary : AggregateRoot
{
    private readonly List<CompositeNode> _composites;
    private readonly List<Strategy> _strategies;
    private readonly Dictionary<Guid, StrategyVersion> _latestVersions;
    private readonly List<StrategyVersion> _newVersions = [];

    /// <param name="revision"></param>
    /// <param name="strategies"></param>
    /// <param name="composites"></param>
    /// <param name="latestVersions">The latest version of each strategy.</param>
    public StrategyLibrary(
        long revision,
        IEnumerable<Strategy> strategies,
        IEnumerable<CompositeNode> composites,
        IEnumerable<StrategyVersion>? latestVersions = null)
    {
        Revision = revision;
        _strategies = [.. strategies];
        _composites = [.. composites];
        _latestVersions = (latestVersions ?? []).ToDictionary(version => version.StrategyId);
    }

    /// <summary>Grows with every saved change; a save based on an older revision is a conflict.</summary>
    public long Revision { get; }

    public IReadOnlyList<Strategy> Strategies => _strategies;

    public IReadOnlyList<CompositeNode> Composites => _composites;

    /// <summary>Versions the operations on this instance created, oldest first; they are saved with the change.</summary>
    public IReadOnlyList<StrategyVersion> NewVersions => _newVersions;

    public StrategyVersion? LatestVersion(Guid strategyId)
    {
        return _latestVersions.GetValueOrDefault(strategyId);
    }

    /// <exception cref="GraphValidationException">The strategy graph is invalid.</exception>
    public Strategy AddStrategy(Guid id, string name, StrategyDefinition definition, DateTimeOffset now)
    {
        Validate(definition);
        var strategy = new Strategy(id, name, definition, now, now);
        _strategies.Add(strategy);
        Version(strategy, Catalog(), now);

        return strategy;
    }

    /// <returns>The changed strategy, or <c>null</c> when there is no such strategy.</returns>
    /// <exception cref="GraphValidationException">The new graph is invalid; the strategy stays as it was.</exception>
    public Strategy? UpdateStrategy(Guid id, string name, StrategyDefinition definition, DateTimeOffset now)
    {
        int index = _strategies.FindIndex(strategy => strategy.Id == id);

        if (index < 0)
        {
            return null;
        }

        Validate(definition);
        _strategies[index] = _strategies[index] with { Name = name, Definition = definition, UpdatedAt = now };
        Version(_strategies[index], Catalog(), now);

        return _strategies[index];
    }

    public bool RemoveStrategy(Guid id)
    {
        return _strategies.RemoveAll(strategy => strategy.Id == id) > 0;
    }

    /// <exception cref="GraphValidationException">The composite graph is invalid.</exception>
    public CompositeNode AddComposite(Guid id, string name, CompositeDefinition definition, DateTimeOffset now)
    {
        ValidateComposite(id, definition, Catalog());
        var composite = new CompositeNode(id, name, definition, now, now);
        _composites.Add(composite);

        return composite;
    }

    /// <summary>The change reaches every graph that uses the node, so it is saved only when all of them stay valid.</summary>
    /// <returns>The changed node, or <c>null</c> when there is no such node.</returns>
    /// <exception cref="GraphValidationException">The new graph of the node is invalid.</exception>
    /// <exception cref="NodeInUseException">A graph that uses the node would become invalid.</exception>
    public CompositeNode? UpdateComposite(Guid id, string name, CompositeDefinition definition, DateTimeOffset now)
    {
        int index = _composites.FindIndex(composite => composite.Id == id);

        if (index < 0)
        {
            return null;
        }

        CompositeCatalog changed = Catalog(id, definition);
        ValidateComposite(id, definition, changed);
        HashSet<Guid> affected = Dependents(id);
        var usages = new List<NodeUsage>();

        foreach (CompositeNode user in _composites.Where(composite => affected.Contains(composite.Id)))
        {
            Collect(UsageKind.Composite, user.Id, user.Name, GraphValidator.ValidateComposite(new CustomNodeId(user.Id), user.Definition, changed), usages);
        }

        foreach (Strategy user in _strategies.Where(strategy => Uses(strategy.Definition.Graph, affected)))
        {
            Collect(UsageKind.Strategy, user.Id, user.Name, GraphValidator.ValidateStrategy(user.Definition, changed), usages);
        }

        if (usages.Count > 0)
        {
            throw new NodeInUseException(usages);
        }

        _composites[index] = _composites[index] with { Name = name, Definition = definition, UpdatedAt = now };

        return _composites[index];
    }

    /// <exception cref="NodeInUseException">A strategy or another composite node uses the node.</exception>
    public bool RemoveComposite(Guid id)
    {
        HashSet<Guid> self = [id];
        var usages = _composites.Where(composite => Uses(composite.Definition.Graph, self))
            .Select(composite => new NodeUsage(UsageKind.Composite, composite.Id, composite.Name, []))
            .Concat(_strategies.Where(strategy => Uses(strategy.Definition.Graph, self))
                .Select(strategy => new NodeUsage(UsageKind.Strategy, strategy.Id, strategy.Name, [])))
            .ToList();

        if (usages.Count > 0)
        {
            throw new NodeInUseException(usages);
        }

        return _composites.RemoveAll(composite => composite.Id == id) > 0;
    }

    /// <returns>The clone as a new custom node, or <c>null</c> when there is no such source node.</returns>
    public CompositeNode? CloneComposite(CompositeNodeId source, Guid id, string name, DateTimeOffset now)
    {
        if (Catalog().Find(source) is not { } definition)
        {
            return null;
        }

        var clone = new CompositeNode(id, name, definition, now, now);
        _composites.Add(clone);

        return clone;
    }

    /// <summary>Built-in and custom composite nodes, for validation and execution.</summary>
    public CompositeCatalog Catalog()
    {
        return Catalog(null, null);
    }

    private CompositeCatalog Catalog(Guid? replacedId, CompositeDefinition? replacement)
    {
        var definitions = BuiltInComposites.All.ToDictionary(composite => (CompositeNodeId)new BuiltInNodeId(composite.Node), composite => composite.Definition);

        foreach (CompositeNode composite in _composites)
        {
            definitions[new CustomNodeId(composite.Id)] = composite.Id == replacedId ? replacement! : composite.Definition;
        }

        return new CompositeCatalog(definitions);
    }

    /// <summary>The node and every custom node that uses it, directly or through other nodes.</summary>
    private HashSet<Guid> Dependents(Guid id)
    {
        HashSet<Guid> found = [id];
        bool grew = true;

        while (grew)
        {
            int before = found.Count;
            found.UnionWith(_composites.Where(composite => Uses(composite.Definition.Graph, found)).Select(composite => composite.Id));
            grew = found.Count > before;
        }

        return found;
    }

    /// <summary>A snapshot that differs from the latest version becomes the next version.</summary>
    private void Version(Strategy strategy, CompositeCatalog catalog, DateTimeOffset now)
    {
        var snapshot = StrategySnapshot.Of(strategy.Definition, catalog);
        StrategyVersion? latest = LatestVersion(strategy.Id);

        if (snapshot.Equals(latest?.Snapshot))
        {
            return;
        }

        var version = new StrategyVersion(strategy.Id, (latest?.Number ?? 0) + 1, snapshot, now);
        _latestVersions[strategy.Id] = version;
        _newVersions.Add(version);
    }

    private static bool Uses(Graph graph, HashSet<Guid> ids)
    {
        return graph.Nodes.Any(node => node.Type is CompositeNodeType { Id: CustomNodeId { Value: var used } } && ids.Contains(used));
    }

    private static void Collect(UsageKind kind, Guid id, string name, IReadOnlyList<GraphError> errors, List<NodeUsage> usages)
    {
        if (errors.Count > 0)
        {
            usages.Add(new NodeUsage(kind, id, name, errors));
        }
    }

    private static void ValidateComposite(Guid id, CompositeDefinition definition, CompositeCatalog catalog)
    {
        IReadOnlyList<GraphError> errors = GraphValidator.ValidateComposite(new CustomNodeId(id), definition, catalog);

        if (errors.Count > 0)
        {
            throw new GraphValidationException(errors);
        }
    }

    private void Validate(StrategyDefinition definition)
    {
        IReadOnlyList<GraphError> errors = GraphValidator.ValidateStrategy(definition, Catalog());

        if (errors.Count > 0)
        {
            throw new GraphValidationException(errors);
        }
    }
}