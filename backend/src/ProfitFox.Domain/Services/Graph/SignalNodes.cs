using System.Globalization;

using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Domain.Services.Graph;

/// <summary>
///     The references of the <c>signal</c> nodes of a graph (spec strategy-graph «Узел сигнала»): the setting
///     <c>signal</c> holds the id of a saved signal, <c>version</c> the number of its version, from 1.
/// </summary>
public static class SignalNodes
{
    /// <summary>
    ///     The reference the node holds, when both settings are written and well formed.
    /// </summary>
    public static bool TryReference(NodeDefinition node, out SignalReference reference)
    {
        reference = default;
        if (node.Kind != NodeCatalog.Signal
            || !node.Settings.TryGetValue(NodeCatalog.SignalSetting, out string? id)
            || !node.Settings.TryGetValue(NodeCatalog.VersionSetting, out string? version)
            || !TryId(id, out Guid signal)
            || !TryVersion(version, out int number))
        {
            return false;
        }

        reference = new SignalReference(signal, number);
        return true;
    }

    /// <summary>
    ///     Every distinct reference of the graph, in the order of its nodes: what the application reads before the checks.
    /// </summary>
    public static IReadOnlyList<SignalReference> References(GraphDefinition graph)
    {
        List<SignalReference> references = [];
        foreach (NodeDefinition node in graph.Nodes)
        {
            if (TryReference(node, out SignalReference reference) && !references.Contains(reference))
            {
                references.Add(reference);
            }
        }

        return references;
    }

    /// <summary>
    ///     Why a free setting of a <c>signal</c> node has a wrong value, or null when it is right.
    /// </summary>
    internal static string? CheckSetting(string name, string value)
    {
        return name switch
        {
            NodeCatalog.SignalSetting when !TryId(value, out _) => $"{name} is the id of a saved signal, not {value}.",
            NodeCatalog.VersionSetting when !TryVersion(value, out _) => $"{name} is the number of a version from 1, not {value}.",
            _ => null
        };
    }

    private static bool TryId(string text, out Guid id)
    {
        return Guid.TryParseExact(text, "D", out id);
    }

    // Written as the number itself: «2», not «02» or «+2», so one version has one text.
    private static bool TryVersion(string text, out int version)
    {
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version)
               && version >= 1
               && version.ToString(CultureInfo.InvariantCulture) == text;
    }
}
