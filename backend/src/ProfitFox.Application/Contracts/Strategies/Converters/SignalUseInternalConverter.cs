using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Application.Contracts.Strategies.Converters;

public static class SignalUseInternalConverter
{
    /// <summary>
    ///     What each signal node of the graph points at, in the order of the nodes; a node whose reference is not well formed
    ///     has an error of its setting and nothing to show.
    /// </summary>
    public static IReadOnlyList<SignalUseInternal> ToSignalUses(this GraphDefinition graph, SignalSet signals)
    {
        List<SignalUseInternal> uses = [];
        foreach (NodeDefinition node in graph.Nodes)
        {
            if (!SignalNodes.TryReference(node, out SignalReference reference))
            {
                continue;
            }

            SignalLookup found = signals.Find(reference);
            uses.Add(new SignalUseInternal(
                node.Id,
                reference.Signal,
                found.Name,
                reference.Version,
                found.LatestVersion,
                found.Signal?.Graph.Parameters ?? []));
        }

        return uses;
    }
}
