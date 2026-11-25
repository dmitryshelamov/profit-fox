using MediatR;

using ProfitFox.Application.Queries.Filters;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Lists;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;
using ProfitFox.Domain.ValueObjects.MarketData.Enums;

namespace ProfitFox.Application.Queries.Market;

/// <summary>
///     The market sorted by the turnover of 24 hours from the largest by default; the time of the snapshot is the latest of
///     its rows.
/// </summary>
internal sealed class GetMarketPairsQueryHandler(IMarketPairRepository market, ISavedFilterRepository filters) : IRequestHandler<GetMarketPairsQuery, MarketPairsResult>
{
    public async Task<MarketPairsResult> Handle(GetMarketPairsQuery request, CancellationToken cancellationToken)
    {
        ListOrder order = FilterResolver.Order(ListFields.Market, "turnover24h", request.List);
        InstrumentKind? kind = FilterResolver.Kind(request.Kind);
        (IReadOnlyList<FilterCondition> conditions, bool gone) = await FilterResolver.ConditionsAsync(filters, request.List.Filter, FilterPlace.Market, cancellationToken);
        ListPage<MarketPairRow> page = await market.ListAsync(
            new MarketListQuery(order, request.Search, request.IdeasOnly, request.Strategy, request.Timeframe, conditions, kind),
            cancellationToken);
        DateTimeOffset? updated = page.Items.Count == 0 ? null : page.Items.Max(row => row.Pair.UpdatedAt);
        return new MarketPairsResult(page, gone, updated);
    }
}
