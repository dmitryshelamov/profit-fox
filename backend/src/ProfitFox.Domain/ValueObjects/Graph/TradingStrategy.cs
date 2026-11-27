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

    private static EntrySide Side(NodeDefinition node)
    {
        return node.Settings[NodeCatalog.Side] == NodeCatalog.Long ? EntrySide.Long : EntrySide.Short;
    }
}
