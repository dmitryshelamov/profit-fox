using ProfitFox.Engine.Strategy.Graph.Model;
using ProfitFox.Engine.Strategy.Nodes;

namespace ProfitFox.Bench.Presentation.Nodes;

/// <summary>
///     The node catalog for the constructor, in the order of the engine.
/// </summary>
public static class NodeCatalogResponse
{
    private static readonly GraphDefinition Empty = new GraphDefinition([], [], [], []);

    public static IReadOnlyList<NodeKindResponse> Build()
    {
        return [.. NodeCatalog.All.Select(Kind)];
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
