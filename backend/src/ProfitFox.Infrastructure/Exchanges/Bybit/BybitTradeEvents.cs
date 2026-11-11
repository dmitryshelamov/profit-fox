using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;

using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>A trade of <c>publicTrade</c> as the domain counts it: the side of the taker; every trade, a block trade and one of the RPI orders too.</summary>
internal static class BybitTradeEvents
{
    public static TradeEvent Of(BybitTrade trade)
    {
        return new TradeEvent(
            new DateTimeOffset(DateTime.SpecifyKind(trade.Timestamp, DateTimeKind.Utc)),
            trade.Side == OrderSide.Buy ? TradeSide.Buy : TradeSide.Sell,
            trade.Quantity,
            trade.Price);
    }
}