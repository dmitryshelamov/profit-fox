using MediatR;

using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Strategies.Execution;

namespace ProfitFox.Application.LiveTrading.Queries.GetBotPositions;

public sealed class GetBotPositionsQueryHandler(IStrategyLibraryRepository library, ILiveBotRepository live)
    : IRequestHandler<GetBotPositionsQuery, IReadOnlyList<LivePositionDetails>?>
{
    public async Task<IReadOnlyList<LivePositionDetails>?> Handle(GetBotPositionsQuery request, CancellationToken cancellationToken)
    {
        if (await library.GetBotAsync(request.Id, cancellationToken) is not { } bot)
        {
            return null;
        }

        IReadOnlyList<LivePosition> positions = await live.GetPositionsAsync(bot.Id, cancellationToken);

        if (positions.Count == 0)
        {
            return [];
        }

        OrderSide entry = await EntrySideAsync(bot, cancellationToken);
        ILookup<int, LiveOrder> orders = (await live.GetOrdersAsync(bot.Id, cancellationToken)).ToLookup(order => order.PositionNumber);
        ILookup<int, LiveFill> fills = (await live.GetFillsAsync(bot.Id, cancellationToken)).ToLookup(fill => fill.OrderNumber);

        return
        [
            .. positions.Select(position =>
            {
                LiveOrderDetails[] own = [.. orders[position.Number].Select(order => new LiveOrderDetails(order, [.. fills[order.Number]]))];
                LiveFill[] filled = [.. own.SelectMany(order => order.Fills)];

                return new LivePositionDetails(
                    position,
                    Status(position),
                    entry == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy,
                    filled.Length > 0 ? filled.Min(fill => fill.ExecutedAt) : null,
                    Average(own.Where(order => order.Order.Side == entry)),
                    Average(own.Where(order => order.Order.Side != entry)),
                    own);
            })
        ];
    }

    private async Task<OrderSide> EntrySideAsync(Bot bot, CancellationToken cancellationToken)
    {
        int version = (await live.GetAsync(bot.Id, cancellationToken)).StrategyVersion!.Value;
        StrategyDirection direction = (await library.GetVersionAsync(bot.StrategyId, version, cancellationToken))!.Snapshot.Definition.Direction;

        return direction == StrategyDirection.Long ? OrderSide.Buy : OrderSide.Sell;
    }

    private static PositionStatus Status(LivePosition position)
    {
        return position.ClosedAt is not null ? PositionStatus.Closed
            : position.Closing ? PositionStatus.Closing
            : position.Open ? PositionStatus.Open
            : PositionStatus.Waiting;
    }

    /// <summary>The average price of the orders by their totals: a trade may not have come yet, the totals have.</summary>
    private static decimal? Average(IEnumerable<LiveOrderDetails> orders)
    {
        LiveOrder[] filled = [.. orders.Select(order => order.Order).Where(order => order.FilledQuantity > 0m)];
        decimal quantity = filled.Sum(order => order.FilledQuantity);

        return quantity > 0m ? filled.Sum(order => order.FilledValue) / quantity : null;
    }
}