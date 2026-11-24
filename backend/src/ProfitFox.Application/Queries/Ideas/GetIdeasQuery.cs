using MediatR;

using ProfitFox.Application.Queries.Filters;
using ProfitFox.Domain.ValueObjects.Lists.Enums;

namespace ProfitFox.Application.Queries.Ideas;

/// <summary>
///     A page of open or closed ideas, or of the ideas of the pair <see cref="Symbol" /> (spec strategy-ideas «Список идей»,
///     spec market-overview «Идеи пары»), with the choices above the table; for a pair also the window of time its ideas
///     touch and the idea whose page is wanted (task 18.22).
/// </summary>
public sealed record GetIdeasQuery(
    IdeaListKind Kind,
    ListRequest List,
    string? Symbol = null,
    string? Search = null,
    string? Strategy = null,
    string? Timeframe = null,
    string? Side = null,
    string? Label = null,
    bool OnlyPositive = false,
    bool OnlyWithStop = false,
    string? ExitReason = null,
    int? PeriodDays = null,
    string? State = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? Around = null) : IRequest<IdeaListResult>;

/// <summary>
///     A page of the summary of the journal with the choices of the closed ideas (spec strategy-ideas «Сводка журнала»).
/// </summary>
public sealed record GetIdeaSummaryQuery(GetIdeasQuery Ideas) : IRequest<IdeaSummaryResult>;
