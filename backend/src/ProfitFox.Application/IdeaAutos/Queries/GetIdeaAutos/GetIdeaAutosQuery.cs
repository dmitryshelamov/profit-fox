using MediatR;

namespace ProfitFox.Application.IdeaAutos.Queries.GetIdeaAutos;

/// <summary>The autos in the order of the list, each with its account and its totals.</summary>
public sealed record GetIdeaAutosQuery : IRequest<IReadOnlyList<IdeaAutoSummary>>;