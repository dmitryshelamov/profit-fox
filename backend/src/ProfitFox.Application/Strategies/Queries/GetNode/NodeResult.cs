using ProfitFox.Application.Strategies.Queries.GetNodeCatalog;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies.Queries.GetNode;

/// <param name="Entry"></param>
/// <param name="Definition">The graph of a composite node; <c>null</c> for a simple node.</param>
public sealed record NodeResult(NodeCatalogEntry Entry, CompositeDefinition? Definition);