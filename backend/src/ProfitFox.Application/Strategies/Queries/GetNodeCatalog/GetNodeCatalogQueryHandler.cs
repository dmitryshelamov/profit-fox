using MediatR;

using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.Services.Strategies.Nodes;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies.Queries.GetNodeCatalog;

public sealed class GetNodeCatalogQueryHandler(IStrategyLibraryRepository repository) : IRequestHandler<GetNodeCatalogQuery, IReadOnlyList<NodeCatalogEntry>>
{
    public async Task<IReadOnlyList<NodeCatalogEntry>> Handle(GetNodeCatalogQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<CompositeNode> custom = await repository.GetCompositesAsync(cancellationToken);

        return [.. Enum.GetValues<SimpleNodeKind>().Select(Simple), .. BuiltInComposites.All.Select(BuiltIn), .. custom.Select(Custom)];
    }

    /// <summary>Pins of nodes that depend on the context or on settings are given for an empty context and default settings.</summary>
    internal static NodeCatalogEntry Simple(SimpleNodeKind kind)
    {
        NodePins pins = SimpleNodeCatalog.Pins(kind, new NodeSettings(), new PinContext([], [], []));

        return new NodeCatalogEntry(StrategyCodes.ToCode(kind), NodeTitles.Of(kind), NodeGroup.Simple, pins.Inputs, pins.Outputs);
    }

    internal static NodeCatalogEntry BuiltIn(BuiltInComposite composite)
    {
        return new NodeCatalogEntry(StrategyCodes.ToCode(composite.Node), composite.Title, NodeGroup.BuiltIn, composite.Definition.Inputs, composite.Definition.Outputs);
    }

    internal static NodeCatalogEntry Custom(CompositeNode composite)
    {
        return new NodeCatalogEntry(composite.Id.ToString(), composite.Name, NodeGroup.Custom, composite.Definition.Inputs, composite.Definition.Outputs);
    }
}