using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Presentation.Contracts.Nodes.Converters;

/// <summary>
///     The node catalogs of the constructors, in the order of the engine. The catalog of a signal has only the kinds a signal
///     may have (spec signal-constructor «Каталог узлов»), so its palette has no position nodes; the catalog of a strategy has
///     every kind but <c>outputs</c> (spec strategy-constructor «Каталог узлов стратегии»).
/// </summary>
public static class NodeCatalogResponse
{
    private static readonly GraphDefinition Empty = new GraphDefinition([], [], [], []);

    public static IReadOnlyList<NodeKindResponse> Build()
    {
        return [.. NodeCatalog.All.Where(kind => kind.AllowedInSignal).Select(Kind)];
    }

    public static IReadOnlyList<NodeKindResponse> BuildStrategy()
    {
        return [.. NodeCatalog.All.Where(kind => kind.Name != NodeCatalog.Outputs).Select(Kind)];
    }

    private static NodeKindResponse Kind(NodeKind kind)
    {
        (string title, string group) = NodeTitles.Of(kind.Name) ?? (kind.Name, NodeTitles.Logic);
        string? fromGraph = kind.Name switch
        {
            NodeCatalog.Parameters => "parameters",
            NodeCatalog.Outputs => "outputs",
            NodeCatalog.Signal => "signal",
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
        return new PinResponse(pin.Name, PinTypes.Name(pin.Type)) { Many = pin.Many };
    }
}
