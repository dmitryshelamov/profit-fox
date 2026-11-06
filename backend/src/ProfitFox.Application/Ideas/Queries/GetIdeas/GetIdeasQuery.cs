using MediatR;

namespace ProfitFox.Application.Ideas.Queries.GetIdeas;

/// <summary>
///     One page of the open ideas and the ones closed in the last 24 hours, by a column of the list (the expectancy, highest first, when none is
///     named); each filter is a code as the user sent it, none takes every idea. <paramref name="SavedFilter" /> is the id of a saved filter that
///     the ideas must meet. <paramref name="ActiveOnly" /> keeps only the ideas awaiting entry and the active ones,
///     <paramref name="HideTradFi" /> drops the ideas of the non-crypto pairs of the scanner. A page is a number from 1 and a size of 50, 100
///     or 200, the first page of 50 when left out.
/// </summary>
public sealed record GetIdeasQuery(
    string? Setup,
    string? Interval,
    string? Type,
    string? Side,
    string? Category,
    bool NonNegativeExpectancy,
    Guid? SavedFilter,
    string? Sort,
    string? Direction,
    int? Page,
    int? PageSize,
    bool ActiveOnly = false,
    bool HideTradFi = false) : IRequest<GetIdeasResult>;