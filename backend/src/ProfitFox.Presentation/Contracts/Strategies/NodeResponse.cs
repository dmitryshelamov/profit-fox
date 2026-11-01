using System.Text.Json.Nodes;

using ProfitFox.Application.Strategies;
using ProfitFox.Application.Strategies.Queries.GetNode;
using ProfitFox.Application.Strategies.Queries.GetNodeCatalog;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <param name="Id"></param>

/// <param name="Title"></param>/// <param name="Group"><c>simple</c>, <c>builtIn</c> or <c>custom</c>.</param>
/// <param name="Inputs"></param>
/// <param name="Outputs"></param>
/// <param name="Settings">The settings of a simple node; empty for a composite node.</param>
/// <param name="Definition">The graph of a composite node; absent for a simple node.</param>
public sealed record NodeResponse(
    string Id,
    string Title,
    string Group,
    IReadOnlyList<PinResponse> Inputs,
    IReadOnlyList<PinResponse> Outputs,
    IReadOnlyList<SettingResponse> Settings,
    JsonObject? Definition)
{
    public static NodeResponse Of(NodeCatalogEntry entry)
    {
        return new NodeResponse(
            entry.Id,
            entry.Title,
            entry.Group switch
            {
                NodeGroup.Simple => "simple",
                NodeGroup.BuiltIn => "builtIn",
                _ => "custom"
            },
            [.. entry.Inputs.Select(PinResponse.Of)],
            [.. entry.Outputs.Select(PinResponse.Of)],
            [.. entry.Settings.Select(SettingResponse.Of)],
            null);
    }

    public static NodeResponse Of(NodeResult node)
    {
        return Of(node.Entry) with { Definition = node.Definition is null ? null : StrategyJson.WriteComposite(node.Definition) };
    }
}