using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Domain.Services.Graph;

/// <summary>
///     The positions a saved signal or strategy keeps: those of the nodes of its graph only (spec signal-constructor
///     «Расположение узлов отдельно от графа»).
/// </summary>
public static class GraphLayout
{
    public static Dictionary<string, NodePosition> Of(GraphDefinition graph, IReadOnlyDictionary<string, NodePosition> layout)
    {
        var ids = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        return layout.Where(position => ids.Contains(position.Key)).ToDictionary(StringComparer.Ordinal);
    }
}
