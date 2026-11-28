using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Filters;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Lists;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Queries.Trades;

/// <summary>
///     A page of the open trades — every state that holds its pair — or of the closed and cancelled ones (change
///     take-idea-trades, design.md, decision 13; spec idea-trades «Раздел „Сделки“»), sorted by a column of
///     <see cref="ListFields.Trades" />: the open by the click from the newest by default, the closed by the exit. With the
///     numbers of both tabs.
/// </summary>
public sealed record GetTradesQuery(bool Open, ListRequest List) : IRequest<TradeListResult>;

/// <summary>
///     A page of trades and the numbers of the open and of the closed.
/// </summary>
public sealed record TradeListResult(ListPage<TradeListItem> Page, int Open, int Closed);

/// <summary>
///     One trade by its id.
/// </summary>
public sealed record GetTradeQuery(Guid Id) : IRequest<Trade>;

internal sealed class GetTradesQueryHandler(ITradeRepository trades) : IRequestHandler<GetTradesQuery, TradeListResult>, IRequestHandler<GetTradeQuery, Trade>
{
    /// <exception cref="IdeasRequestException">The field of sorting or the page is refused.</exception>
    public async Task<TradeListResult> Handle(GetTradesQuery request, CancellationToken cancellationToken)
    {
        ListOrder order = FilterResolver.Order(ListFields.Trades, request.Open ? "created" : "exitTime", request.List);
        ListPage<TradeListItem> page = await trades.ListAsync(new TradeListQuery(request.Open, order), cancellationToken);
        (int open, int closed) = await trades.CountsAsync(cancellationToken);
        return new TradeListResult(page, open, closed);
    }

    /// <exception cref="KeyNotFoundException">There is no trade of the id.</exception>
    public async Task<Trade> Handle(GetTradeQuery request, CancellationToken cancellationToken)
    {
        return await trades.FindAsync(request.Id, cancellationToken) ?? throw new KeyNotFoundException($"There is no trade {request.Id}.");
    }
}
