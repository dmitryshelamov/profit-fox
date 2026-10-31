using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies.Queries.GetNodeCatalog;

/// <param name="Id">The kind code of a simple node, the code of a built-in node or the id of a custom node.</param>
/// <param name="Title"></param>
/// <param name="Group"></param>
/// <param name="Inputs"></param>
/// <param name="Outputs"></param>
public sealed record NodeCatalogEntry(string Id, string Title, NodeGroup Group, IReadOnlyList<PinDeclaration> Inputs, IReadOnlyList<PinDeclaration> Outputs);