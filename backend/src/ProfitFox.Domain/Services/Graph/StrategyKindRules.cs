using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Domain.Services.Graph;

/// <summary>
///     What the kind of a strategy asks of its graph on top of the checks of a strategy (spec strategy-graph «Вид
///     стратегии»; change add-strategy-backtest, design.md, decision 1). A strategy for a bot asks nothing more. A category
///     of ideas is a strategy by plan (spec strategy-position «Стратегия по плану»): it has a <c>close-after</c>, no
///     <c>market-order</c>, <c>limit-order</c> or <c>close-position</c>, and no wire from the state of the position — a
///     random entry needs a time, since a trade outside the graph has no other end. Whether a strategy is by plan is told
///     by the same rules: it is by plan when a category of ideas would have no error.
/// </summary>
public static class StrategyKindRules
{
    private static readonly string[] OrderKinds = [NodeCatalog.MarketOrder, NodeCatalog.LimitOrder, NodeCatalog.ClosePosition];

    /// <summary>
    ///     Whether a strategy of <paramref name="kind" /> may have a node of the kind <paramref name="node" />: the palette
    ///     of the editor offers only these.
    /// </summary>
    public static bool Allows(StrategyKind kind, string node)
    {
        return kind != StrategyKind.Idea || !OrderKinds.Contains(node, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Every rule of <paramref name="kind" /> the graph breaks, in the order of the graph: the nodes, the time, the
    ///     wires.
    /// </summary>
    public static List<GraphError> Check(GraphDefinition graph, StrategyKind kind)
    {
        List<GraphError> errors = [];
        if (kind != StrategyKind.Idea)
        {
            return errors;
        }

        foreach (NodeDefinition node in graph.Nodes.Where(node => !Allows(kind, node.Kind)))
        {
            errors.Add(
                new GraphError(GraphErrorCode.StrategyKindNodeNotAllowed, $"Узел {node.Kind} недоступен стратегии вида «категория идей»: она входит узлом {NodeCatalog.Enter}.")
                {
                    Node = node.Id
                });
        }

        if (graph.Nodes.All(node => node.Kind != NodeCatalog.CloseAfter))
        {
            errors.Add(new GraphError(GraphErrorCode.StrategyKindNeedsTime, $"Стратегии вида «категория идей» нужен срок: узел {NodeCatalog.CloseAfter}."));
        }

        var positions = new HashSet<string>(graph.Nodes.Where(node => node.Kind == NodeCatalog.Position).Select(node => node.Id), StringComparer.Ordinal);
        foreach (WireDefinition wire in graph.Wires.Where(wire => positions.Contains(wire.From.Node) && wire.From.Pin != NodeCatalog.PositionPin))
        {
            errors.Add(
                new GraphError(GraphErrorCode.StrategyKindReadsPosition, $"Стратегия вида «категория идей» не читает состояние позиции: провод от {wire.From}.")
                {
                    Wire = wire
                });
        }

        return errors;
    }

    /// <summary>
    ///     The kind a strategy saved before it had one reads with (spec strategy-constructor «Вид у прежних стратегий»): a
    ///     category of ideas when its graph is a strategy by plan, a strategy for a bot otherwise.
    /// </summary>
    public static StrategyKind Of(GraphDefinition graph)
    {
        return IsByPlan(graph) ? StrategyKind.Idea : StrategyKind.Bot;
    }

    /// <summary>
    ///     Whether the graph is a strategy by plan: a category of ideas would have no error.
    /// </summary>
    public static bool IsByPlan(GraphDefinition graph)
    {
        return Check(graph, StrategyKind.Idea).Count == 0;
    }
}
