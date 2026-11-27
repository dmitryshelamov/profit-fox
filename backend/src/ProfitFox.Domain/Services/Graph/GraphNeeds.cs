using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Domain.Services.Graph;

/// <summary>
///     What data a graph needs besides candles and funding (change add-idea-score, design.md, decision 6): the points of open
///     interest are read only for a graph with the node «OI». The graph is the one the executor gets — a strategy after its
///     <c>signal</c> nodes are expanded — so a node inside a signal counts.
/// </summary>
public static class GraphNeeds
{
    public static bool OpenInterest(GraphDefinition graph)
    {
        return graph.Nodes.Any(node => node.Kind == NodeCatalog.OpenInterest);
    }
}
