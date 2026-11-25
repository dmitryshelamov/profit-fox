using MediatR;

using ProfitFox.Application.Queries.Filters;
using ProfitFox.Domain.ValueObjects.Lists;

namespace ProfitFox.Application.Queries.Market;

/// <summary>
///     A page of the pairs of the market (spec market-overview «Список пар рынка»): a part of the symbol, only pairs with an
///     open idea, the strategy and the timeframe of an idea, a saved filter of «Рынок», and the kind of the pair:
///     <c>crypto</c> or <c>stock</c> (change add-instrument-kind-filter, decision 4).
/// </summary>
public sealed record GetMarketPairsQuery(ListRequest List, string? Search = null, bool IdeasOnly = false, string? Strategy = null, string? Timeframe = null, string? Kind = null)
    : IRequest<MarketPairsResult>;

/// <summary>
///     A page of the market, whether the filter asked for is gone, and when the snapshot was last written.
/// </summary>
public sealed record MarketPairsResult(ListPage<MarketPairRow> Page, bool FilterGone, DateTimeOffset? UpdatedAt);
