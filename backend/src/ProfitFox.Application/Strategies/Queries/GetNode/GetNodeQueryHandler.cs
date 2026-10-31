using MediatR;

using ProfitFox.Application.Strategies.Queries.GetNodeCatalog;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies.Queries.GetNode;

public sealed class GetNodeQueryHandler(IStrategyLibraryRepository repository) : IRequestHandler<GetNodeQuery, NodeResult?>
{
    public async Task<NodeResult?> Handle(GetNodeQuery request, CancellationToken cancellationToken)
    {
        if (StrategyCodes.TryParse(request.Id, out SimpleNodeKind kind))
        {
            return new NodeResult(GetNodeCatalogQueryHandler.Simple(kind), null);
        }

        StrategyJson.TryParseNodeId(request.Id, out CompositeNodeId? id);

        switch (id)
        {
            case BuiltInNodeId builtIn:
                BuiltInComposite composite = BuiltInComposites.All.Single(candidate => candidate.Node == builtIn.Node);

                return new NodeResult(GetNodeCatalogQueryHandler.BuiltIn(composite), composite.Definition);
            case CustomNodeId custom:
                CompositeNode? node = await repository.GetCompositeAsync(custom.Value, cancellationToken);

                return node is null ? null : new NodeResult(GetNodeCatalogQueryHandler.Custom(node), node.Definition);
            default:
                return null;
        }
    }
}