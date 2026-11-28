using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;

using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Infrastructure.Clients.Bybit;

/// <summary>
///     The models of <c>Bybit.Net</c> as the messages of the ports (change take-idea-trades, design.md, decision 3). A message
///     of the stream is a model of REST with a field or two more (<c>BybitOrderUpdate : BybitOrder</c>, <c>BybitUserTradeUpdate
///     : BybitUserTrade</c>, <c>BybitPositionUpdate : BybitPosition</c>), so one translation serves both paths and a message
///     comes out field for field the same whichever way it came. An empty field of Bybit stays empty: a stop or a price of
///     nought is no stop and no price.
/// </summary>
internal static class BybitMessages
{
    public static ExchangeFill Of(BybitUserTrade trade)
    {
        return new ExchangeFill(
            trade.TradeId,
            trade.Symbol,
            Text(trade.ClientOrderId),
            Side(trade.Side),
            trade.Price,
            trade.Quantity,
            trade.Fee ?? 0m,
            trade.TradeType switch
            {
                TradeType.Trade => ExchangeFillType.Trade,
                TradeType.BustTrade => ExchangeFillType.Liquidation,
                TradeType.AdlTrade => ExchangeFillType.Adl,
                TradeType.Funding => ExchangeFillType.Funding,
                _ => ExchangeFillType.Other
            },
            trade.StopOrderType switch
            {
                null or StopOrderType.Unknown => ExchangeStopType.None,
                StopOrderType.StopLoss or StopOrderType.PartialStopLoss => ExchangeStopType.StopLoss,
                StopOrderType.TakeProfit or StopOrderType.PartialTakeProfit => ExchangeStopType.TakeProfit,
                _ => ExchangeStopType.Other
            },
            new DateTimeOffset(DateTime.SpecifyKind(trade.Timestamp, DateTimeKind.Utc)));
    }

    public static ExchangeOrderUpdate Of(BybitOrder order)
    {
        return new ExchangeOrderUpdate(
            order.Symbol,
            Text(order.ClientOrderId),
            order.OrderId,
            order.Status switch
            {
                OrderStatus.PartiallyFilled => ExchangeOrderState.PartiallyFilled,
                OrderStatus.Filled => ExchangeOrderState.Filled,
                OrderStatus.Rejected => ExchangeOrderState.Rejected,
                OrderStatus.Cancelled or OrderStatus.PartiallyFilledCanceled or OrderStatus.Deactivated => ExchangeOrderState.Cancelled,
                _ => ExchangeOrderState.New
            },
            order.QuantityFilled ?? 0m,
            Text(order.RejectReason) is { } reason && reason != "EC_NoError" ? reason : order.CancelType is { } cancel && cancel.ToString() is not ("None" or "Unknown") ? cancel.ToString() : null);
    }

    public static ExchangePosition Of(BybitPosition position)
    {
        return new ExchangePosition(
            position.Symbol,
            position.Side switch
            {
                PositionSide.Buy => TradeSide.Long,
                PositionSide.Sell => TradeSide.Short,
                _ => null
            },
            position.Quantity,
            Positive(position.AveragePrice),
            Positive(position.StopLoss),
            Positive(position.TakeProfit),
            Positive(position.Leverage),
            position.PositionIdx == PositionIdx.OneWayMode,
            position.UnrealizedPnl,
            Positive(position.MarkPrice));
    }

    public static TradeSide Side(OrderSide side)
    {
        return side == OrderSide.Buy ? TradeSide.Long : TradeSide.Short;
    }

    public static OrderSide Side(TradeSide side)
    {
        return side == TradeSide.Long ? OrderSide.Buy : OrderSide.Sell;
    }

    private static decimal? Positive(decimal? value)
    {
        return value is > 0 ? value : null;
    }

    private static string? Text(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
