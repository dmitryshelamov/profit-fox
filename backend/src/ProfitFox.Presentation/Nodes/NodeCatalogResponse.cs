using ProfitFox.Domain.Graph.Model;
using ProfitFox.Domain.Nodes;

namespace ProfitFox.Presentation.Nodes;

/// <summary>
///     The node catalog for the constructor of signals, in the order of the engine: only the kinds a signal may have
///     (spec signal-constructor «Каталог узлов»), so the palette has no position nodes.
/// </summary>
public static class NodeCatalogResponse
{
    private static readonly GraphDefinition Empty = new GraphDefinition([], [], [], []);

    public static IReadOnlyList<NodeKindResponse> Build()
    {
        return [.. NodeCatalog.All.Where(kind => kind.AllowedInSignal).Select(Kind)];
    }

    private static NodeKindResponse Kind(NodeKind kind)
    {
        (string title, string group) = NodeTitles.Of(kind.Name) ?? (kind.Name, NodeTitles.Logic);
        string? fromGraph = kind.Name switch
        {
            NodeCatalog.Parameters => "parameters",
            NodeCatalog.Outputs => "outputs",
            _ => null
        };
        return new NodeKindResponse(
            kind.Name,
            title,
            group,
            [.. kind.Inputs(Empty).Select(Pin)],
            [.. kind.Outputs(Empty).Select(Pin)],
            [.. kind.Settings.Select(setting => new SettingResponse(setting.Name, setting.Values, setting.Default))],
            fromGraph);
    }

    private static PinResponse Pin(PinDeclaration pin)
    {
        return new PinResponse(pin.Name, PinTypes.Name(pin.Type));
    }
}
