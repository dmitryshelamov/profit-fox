using MediatR;

namespace ProfitFox.Application.Strategies.Queries.GetNode;

/// <summary>A node by its catalog id, or <c>null</c> when there is none.</summary>
public sealed record GetNodeQuery(string Id) : IRequest<NodeResult?>;