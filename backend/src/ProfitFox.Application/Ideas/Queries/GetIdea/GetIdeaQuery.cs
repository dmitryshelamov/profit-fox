using MediatR;

namespace ProfitFox.Application.Ideas.Queries.GetIdea;

/// <summary>An idea with the statistics of its bucket; none when there is no such idea.</summary>
public sealed record GetIdeaQuery(Guid Id) : IRequest<IdeaView?>;