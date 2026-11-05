using MediatR;

namespace ProfitFox.Application.Market.Queries.GetMarket;

/// <summary>
///     One page of the market snapshot and the state of the scanner, from storage only; each filter is a code as the user sent it, none takes
///     every pair. <paramref name="Setup" /> is a setup the pair holds on <paramref name="Interval" /> (15 minutes when left out), and
///     <paramref name="Search" /> a part of the symbol; the order is a column and a direction, the change in 1 hour, highest first, when left out.
/// </summary>
public sealed record GetMarketQuery(
    string? Category,
    string? Search,
    string? Setup,
    string? Interval,
    Guid? SavedFilter,
    string? Sort,
    string? Direction,
    int? Page,
    int? PageSize) : IRequest<GetMarketResult>;