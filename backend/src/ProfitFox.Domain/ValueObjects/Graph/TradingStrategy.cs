using System.Globalization;

using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.ValueObjects.Graph;

/// <summary>
///     A checked strategy (spec strategy-graph «Стратегия», «Узел позиции в стратегии»): a graph without declared outputs
///     with one node <c>position</c>, a node that opens it and one that closes it, every order wired into it, and at most
///     one <c>stop-loss</c>, one <c>take-profit</c> and one <c>close-after</c>. It
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

    /// <summary>
    ///     Whether the strategy is by plan (spec strategy-position «Стратегия по плану»): it has a <c>close-after</c>, no
    ///     <c>market-order</c>, <c>limit-order</c> or <c>close-position</c>, and no wire from the state of the position. Only
    ///     its <c>enter</c> opens the position and only the time, the stop and the target close it, so its graph is computed
    ///     ahead of the minutes; any other strategy is a strategy with orders. The rules are those of a category of ideas
    ///     (<see cref="StrategyKindRules" />).
    /// </summary>
    public bool ByPlan { get; }

    internal TradingStrategy(GraphDefinition graph)
        : this(graph, graph)
    {
    }

    internal TradingStrategy(GraphDefinition source, GraphDefinition graph)
    {
        Source = source;
        Graph = graph;
        Entries = [.. graph.Nodes.Where(node => node.Kind == NodeCatalog.Enter).Select(node => new StrategyEntry(node.Id, Side(node)))];
        ByPlan = StrategyKindRules.IsByPlan(graph);
    }

    /// <summary>
    ///     The size of the entry of the strategy of a hypothesis, in USDT (change add-position-node, design.md, decision
    ///     10): a hypothesis is counted in fractions, so the size does not change its result.
    /// </summary>
    public const decimal HypothesisEntrySize = 100m;

    /// <summary>
    ///     The strategy of a signal and the values of a trade plan (change add-strategy-graph, design.md, decision 8; change
    ///     add-position-node, decision 10): the event of the signal goes into an <c>enter</c> node of the side and of the
    ///     size <see cref="HypothesisEntrySize" /> instead of the output, the order of the entry into a node
    ///     <c>position</c>, <c>close-after</c> gets the time, and <c>stop-loss</c> and <c>take-profit</c> are added when
    ///     given; each exit node reads the position and gives its order to it. A new node is named by its kind, with a number
    ///     when the signal has the id. The signal is not changed.
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
        nodes.Add(
            new NodeDefinition(
                enter,
                NodeCatalog.Enter,
                Settings(NodeCatalog.Side, side == EntrySide.Long ? NodeCatalog.Long : NodeCatalog.Short),
                Inputs(NodeCatalog.Size, GraphValue.FromNumber(HypothesisEntrySize))));
        string position = FreeId(NodeCatalog.Position, ids);
        nodes.Add(new NodeDefinition(position, NodeCatalog.Position, Settings(), Inputs()));
        List<NodeDefinition> exits = [Exit(NodeCatalog.CloseAfter, NodeCatalog.After, GraphValue.FromDuration(after), ids)];

        if (stop is { } stopDistance)
        {
            exits.Add(Exit(NodeCatalog.StopLoss, NodeCatalog.Distance, GraphValue.FromNumber(stopDistance), ids));
        }

        if (target is { } targetDistance)
        {
            exits.Add(Exit(NodeCatalog.TakeProfit, NodeCatalog.Distance, GraphValue.FromNumber(targetDistance), ids));
        }

        nodes.AddRange(exits);

        // The wire that went into the output of the signal goes into the entry, at the same place among the wires; then
        // the order of the entry into the position, and for each exit the position into it and its order back.
        var entry = new PinReference(enter, NodeCatalog.When);
        var orders = new PinReference(position, NodeCatalog.Orders);
        List<WireDefinition> wires = [.. graph.Wires.Select(wire => wire.To.Node == outputs.Id ? new WireDefinition(wire.From, entry) : wire)];
        wires.Add(new WireDefinition(new PinReference(enter, NodeCatalog.Order), orders));
        foreach (NodeDefinition exit in exits)
        {
            wires.Add(new WireDefinition(new PinReference(position, NodeCatalog.PositionPin), new PinReference(exit.Id, NodeCatalog.PositionPin)));
            wires.Add(new WireDefinition(new PinReference(exit.Id, NodeCatalog.Order), orders));
        }
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
