using MediatR;

namespace ProfitFox.Application.IdeaAutos.Queries.GetIdeaAutoRefusals;

/// <summary>
///     The refusals of the autos from the newest: <c>today</c> since 00:00 UTC or <c>7d</c> for the last 7 days, those of one auto or of all, a page of
///     them by the rules of <c>GET /api/ideas</c>.
/// </summary>
public sealed record GetIdeaAutoRefusalsQuery(string? Period, Guid? AutoId, int? Page, int? PageSize) : IRequest<GetIdeaAutoRefusalsResult>;