using MediatR;

namespace ProfitFox.Application.Strategies.Queries.GetNodeCatalog;

/// <summary>Simple, built-in and custom nodes the user builds strategies from.</summary>
public sealed record GetNodeCatalogQuery : IRequest<IReadOnlyList<NodeCatalogEntry>>;