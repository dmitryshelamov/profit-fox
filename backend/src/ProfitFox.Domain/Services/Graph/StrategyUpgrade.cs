using System.Globalization;

using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Domain.Services.Graph;

/// <summary>
///     The lift of a saved strategy of the form before change add-position-node (design.md, decision 11; spec
///     strategy-constructor «Подъём графа прежнего вида»): a graph without a node <c>position</c> gets one, each
///     <c>enter</c> without a size gets the size 100 and its order wired into the position, and each <c>stop-loss</c>,
///     <c>take-profit</c> and <c>close-after</c> gets the position and gives its order to it. The new wires follow the old
///     ones in the order of the nodes, so every reading gives the same graph. Nothing else changes; the version in the
///     store is not rewritten.
/// </summary>
public static class StrategyUpgrade
{
    /// <summary>
    ///     The graph lifted; a graph that has a node <c>position</c> is returned as it is.
    /// </summary>
    public static GraphDefinition Raise(GraphDefinition graph)
    {
        if (graph.Nodes.Any(node => node.Kind == NodeCatalog.Position))
        {
            return graph;
        }

        var ids = new HashSet<string>(graph.Nodes.Select(node => node.Id), StringComparer.Ordinal);
        string position = NodeCatalog.Position;
        for (int number = 2; ids.Contains(position); number++)
        {
            position = string.Create(CultureInfo.InvariantCulture, $"{NodeCatalog.Position}-{number}");
        }

        var orders = new PinReference(position, NodeCatalog.Orders);
        var state = new PinReference(position, NodeCatalog.PositionPin);
        List<NodeDefinition> nodes = [];
        List<WireDefinition> wires = [.. graph.Wires];
        foreach (NodeDefinition node in graph.Nodes)
        {
            switch (node.Kind)
            {
                case NodeCatalog.Enter:
                    nodes.Add(node.Inputs.ContainsKey(NodeCatalog.Size) ? node : node with { Inputs = WithSize(node) });
                    wires.Add(new WireDefinition(new PinReference(node.Id, NodeCatalog.Order), orders));
                    break;
                case NodeCatalog.StopLoss or NodeCatalog.TakeProfit or NodeCatalog.CloseAfter:
                    nodes.Add(node);
                    wires.Add(new WireDefinition(state, new PinReference(node.Id, NodeCatalog.PositionPin)));
                    wires.Add(new WireDefinition(new PinReference(node.Id, NodeCatalog.Order), orders));
                    break;
                default:
                    nodes.Add(node);
                    break;
            }
        }

        nodes.Add(new NodeDefinition(position, NodeCatalog.Position, new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, GraphValue>(StringComparer.Ordinal)));
        return graph with { Nodes = nodes, Wires = wires };
    }

    private static Dictionary<string, GraphValue> WithSize(NodeDefinition node)
    {
        return new Dictionary<string, GraphValue>(node.Inputs, StringComparer.Ordinal) { [NodeCatalog.Size] = GraphValue.FromNumber(StrategyStep.DefaultSize) };
    }
}
