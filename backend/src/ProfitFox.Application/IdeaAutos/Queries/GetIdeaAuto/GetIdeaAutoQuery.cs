using MediatR;

namespace ProfitFox.Application.IdeaAutos.Queries.GetIdeaAuto;

/// <summary>One auto with the name of its account; none for an auto there is not or one that was deleted.</summary>
public sealed record GetIdeaAutoQuery(Guid Id) : IRequest<IdeaAutoSummary?>;