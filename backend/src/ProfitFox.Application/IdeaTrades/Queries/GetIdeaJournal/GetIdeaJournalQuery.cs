using MediatR;

namespace ProfitFox.Application.IdeaTrades.Queries.GetIdeaJournal;

/// <summary>
///     The journal of the trades by ideas: the open ones, a page of the closed ones and the totals of all the closed ones. Each filter is a code as the
///     user sent it, none takes every trade: the setup, the side, a part of the symbol, the badge of the promise (<c>up</c>, <c>flat</c>, <c>down</c> or
///     <c>none</c>) and the period of the time of the close, 7, 30 or 90 days. A page is a number from 1 and a size of 50, 100 or 200.
/// </summary>
public sealed record GetIdeaJournalQuery(string? Setup, string? Symbol, string? Side, string? Badge, int? Days, int? Page, int? PageSize) : IRequest<GetIdeaJournalResult>;