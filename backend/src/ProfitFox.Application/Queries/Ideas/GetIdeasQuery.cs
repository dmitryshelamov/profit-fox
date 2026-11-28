using MediatR;

using ProfitFox.Application.Queries.Filters;
using ProfitFox.Domain.ValueObjects.Lists.Enums;

namespace ProfitFox.Application.Queries.Ideas;

/// <summary>
///     A page of open or closed ideas, or of the ideas of the pair <see cref="Symbol" /> (spec strategy-ideas «Список идей»,
///     spec market-overview «Идеи пары»), with the choices above the table; for a pair also the window of time its ideas
///     touch and the idea whose page is wanted (task 18.22). <see cref="PairKind" />: the kind of the pair, <c>crypto</c> or
///     <c>stock</c> (change add-instrument-kind-filter, decision 4); the ideas of a pair take none. <see cref="MinScore" />: the
///     least score, a number with at most two digits after the point (change add-idea-score; change
///     decimal-label-weights).
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
    Guid? Around = null,
    string? PairKind = null,
    string? MinScore = null) : IRequest<IdeaListResult>;

/// <summary>
///     The outcomes of the closed ideas by score with the choices, the period and the saved filter of the journal (change
///     add-idea-score, spec idea-score «Итоги по баллу»).
/// </summary>
public sealed record GetIdeaScoresQuery(GetIdeasQuery Ideas) : IRequest<IdeaScoresResult>;

/// <summary>
///     A page of the summary of the journal with the choices of the closed ideas (spec strategy-ideas «Сводка журнала»).
/// </summary>
public sealed record GetIdeaSummaryQuery(GetIdeasQuery Ideas) : IRequest<IdeaSummaryResult>;
