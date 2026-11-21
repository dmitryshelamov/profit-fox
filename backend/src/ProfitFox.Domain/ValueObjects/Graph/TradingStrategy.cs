using System.Globalization;

using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.ValueObjects.Graph;

/// <summary>
///     A checked strategy (spec strategy-graph «Стратегия»): a graph without declared outputs that has at least one
///     <c>enter</c> node, exactly one <c>close-after</c> and at most one <c>stop-loss</c> and one <c>take-profit</c>. It
///     keeps the graph twice (change add-strategy-constructor, design.md, decision 3): as written, with its <c>signal</c>
///     nodes, and expanded, which the executor runs. The name is not <c>Strategy</c>: that is the namespace.
/// </summary>
public sealed class TradingStrategy
{
    /// <summary>
    ///     The graph as written: with its <c>signal</c> nodes, not expanded.
    /// </summary>
    public GraphDefinition Source { get; }

    /// <summary>
    ///     The graph the executor runs: each <c>signal</c> node replaced with the nodes of its version. Without
    ///     <c>signal</c> nodes it is <see cref="Source" /> itself.
    /// </summary>
    public GraphDefinition Graph { get; }

    /// <summary>
    ///     The <c>enter</c> nodes in the order of the graph, each with the side written in it.
    /// </summary>
    public IReadOnlyList<StrategyEntry> Entries { get; }

    internal TradingStrategy(GraphDefinition graph)
        : this(graph, graph)
    {
    }

    internal TradingStrategy(GraphDefinition source, GraphDefinition graph)
    {
        Source = source;
        Graph = graph;
        Entries = [.. graph.Nodes.Where(node => node.Kind == NodeCatalog.Enter).Select(node => new StrategyEntry(node.Id, Side(node)))];
    }

    /// <summary>
    ///     The strategy of a signal and the values of a trade plan (change add-strategy-graph, design.md, decision 8): the
    ///     event of the signal goes into an <c>enter</c> node of the side instead of the output, <c>close-after</c> gets the
    ///     time, and <c>stop-loss</c> and <c>take-profit</c> are added when given. A new node is named by its kind, with a
    ///     number when the signal has the id. The signal is not changed.
    /// </summary>
    public static TradingStrategy FromSignal(Signal signal, EntrySide side, TimeSpan after, decimal? stop = null, decimal? target = null)
    {
        if (side is not (EntrySide.Long or EntrySide.Short))
        {
            throw new ArgumentOutOfRangeException(nameof(side), side, "An entry is long or short.");
        }

        GraphDefinition graph = signal.Graph;
        NodeDefinition outputs = graph.Nodes.Single(node => node.Kind == NodeCatalog.Outputs);
        List<NodeDefinition> nodes = [.. graph.Nodes.Where(node => !ReferenceEquals(node, outputs))];
        var ids = new HashSet<string>(nodes.Select(node => node.Id), StringComparer.Ordinal);

        string enter = FreeId(NodeCatalog.Enter, ids);
        nodes.Add(new NodeDefinition(enter, NodeCatalog.Enter, Settings(NodeCatalog.Side, side == EntrySide.Long ? NodeCatalog.Long : NodeCatalog.Short), Inputs()));
        nodes.Add(Exit(NodeCatalog.CloseAfter, NodeCatalog.After, GraphValue.FromDuration(after), ids));

        if (stop is { } stopDistance)
        {
            nodes.Add(Exit(NodeCatalog.StopLoss, NodeCatalog.Distance, GraphValue.FromNumber(stopDistance), ids));
        }

        if (target is { } targetDistance)
        {
            nodes.Add(Exit(NodeCatalog.TakeProfit, NodeCatalog.Distance, GraphValue.FromNumber(targetDistance), ids));
        }

        // The wire that went into the output of the signal goes into the entry, at the same place among the wires.
        var entry = new PinReference(enter, NodeCatalog.When);
        List<WireDefinition> wires = [.. graph.Wires.Select(wire => wire.To.Node == outputs.Id ? new WireDefinition(wire.From, entry) : wire)];
        var strategy = new GraphDefinition(graph.Parameters, [], nodes, wires);

        List<GraphError> errors = GraphValidator.ValidateStrategy(strategy);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"The strategy of a signal does not pass the checks: {string.Join("; ", errors)}");
        }

        return new TradingStrategy(strategy);
    }

    private static NodeDefinition Exit(string kind, string input, GraphValue value, HashSet<string> ids)
    {
        return new NodeDefinition(FreeId(kind, ids), kind, Settings(), Inputs(input, value));
    }

    // The kind itself, or the kind with the first number that is free: enter, enter-1, enter-2.
    private static string FreeId(string kind, HashSet<string> ids)
    {
        string id = kind;

        for (int number = 1; !ids.Add(id); number++)
        {
            id = string.Create(CultureInfo.InvariantCulture, $"{kind}-{number}");
        }

        return id;
    }

    private static Dictionary<string, string> Settings(string? name = null, string? value = null)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);

        if (name is not null)
        {
            settings.Add(name, value!);
        }

        return settings;
    }

    private static Dictionary<string, GraphValue> Inputs(string? name = null, GraphValue value = default)
    {
        var inputs = new Dictionary<string, GraphValue>(StringComparer.Ordinal);

        if (name is not null)
        {
            inputs.Add(name, value);
        }

        return inputs;
    }

    private static EntrySide Side(NodeDefinition node)
    {
        return node.Settings[NodeCatalog.Side] == NodeCatalog.Long ? EntrySide.Long : EntrySide.Short;
    }
}
