using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Objects.Models.V5;
using Bybit.Net.Objects.Options;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using ExchangeOrderState = ProfitFox.Application.Ports.LiveTrading.ExchangeOrderState;
using Interval = ProfitFox.Domain.Entities.MarketData.Enums.Interval;
using OrderStatus = Bybit.Net.Enums.OrderStatus;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Bybit v5 streams, a socket client of their own each: the public kline of an instrument and the private order and
///     execution topics of an account. CryptoExchange.Net reconnects; a restored connection is told to the subscriber.
/// </summary>
internal sealed class BybitStreams(ILoggerFactory loggers, BybitSettings settings) : IExchangeStreams
{
    public async Task<IAsyncDisposable> SubscribeCandlesAsync(Instrument instrument, Interval interval, Action<Candle> closed, Action restored, CancellationToken cancellationToken)
    {
        var client = new BybitSocketClient(Options.Create(new BybitSocketOptions { Environment = settings.Environment() }), loggers);
        WebSocketResult<UpdateSubscription> result = instrument.Category == MarketCategory.Spot
            ? await client.V5SpotApi.SubscribeToKlineUpdatesAsync(instrument.Symbol, interval.ToBybit(), update => Closed(update.Data, closed), cancellationToken)
            : await client.V5LinearApi.SubscribeToKlineUpdatesAsync(instrument.Symbol, interval.ToBybit(), update => Closed(update.Data, closed), cancellationToken);

        return Watch(client, restored, result);
    }

    public async Task<IAsyncDisposable> SubscribeAccountAsync(
        ExchangeKey key,
        Action<ExchangeOrder> order,
        Action<ExchangeFill> fill,
        Action restored,
        CancellationToken cancellationToken)
    {
        var client = new BybitSocketClient(
            Options.Create(new BybitSocketOptions { Environment = settings.Environment(), ApiCredentials = new BybitCredentials(key.ApiKey, key.Secret) }),
            loggers);
        WebSocketResult<UpdateSubscription> orders = await client.V5PrivateApi.SubscribeToOrderUpdatesAsync(
            update =>
            {
                foreach (BybitOrderUpdate item in update.Data)
                {
                    order(Order(item));
                }
            },
            cancellationToken);
        Subscription orderStream = Watch(client, restored, orders);
        WebSocketResult<UpdateSubscription> trades = await client.V5PrivateApi.SubscribeToUserTradeUpdatesAsync(
            update =>
            {
                foreach (BybitUserTradeUpdate trade in update.Data)
                {
                    fill(new ExchangeFill(trade.TradeId, trade.ClientOrderId, trade.Quantity, trade.Price, trade.Fee ?? 0m, trade.FeeAsset, Utc(trade.Timestamp)));
                }
            },
            cancellationToken);

        if (!trades.Success)
        {
            await orderStream.DisposeAsync();

            throw new ExchangeUnavailableException($"Bybit refused the stream: {trades.Error!.Message}");
        }

        trades.Data.ConnectionRestored += _ => restored();

        return orderStream;
    }

    private static Subscription Watch(BybitSocketClient client, Action restored, WebSocketResult<UpdateSubscription> result)
    {
        if (!result.Success)
        {
            client.Dispose();

            throw new ExchangeUnavailableException($"Bybit refused the stream: {result.Error!.Message}");
        }

        result.Data.ConnectionRestored += _ => restored();

        return new Subscription(client);
    }

    private static void Closed(IEnumerable<BybitKlineUpdate> klines, Action<Candle> closed)
    {
        foreach (BybitKlineUpdate kline in klines.Where(kline => kline.Confirm))
        {
            closed(new Candle(
                Utc(kline.StartTime),
                new Price(kline.OpenPrice),
                new Price(kline.HighPrice),
                new Price(kline.LowPrice),
                new Price(kline.ClosePrice),
                new Volume(kline.Volume),
                new Volume(kline.Turnover)));
        }
    }

    private static ExchangeOrder Order(BybitOrderUpdate order)
    {
        ExchangeOrderState state = order.Status switch
        {
            OrderStatus.Filled => ExchangeOrderState.Filled,
            OrderStatus.Cancelled or OrderStatus.PartiallyFilledCanceled or OrderStatus.Deactivated => ExchangeOrderState.Cancelled,
            OrderStatus.Rejected => ExchangeOrderState.Rejected,
            _ => ExchangeOrderState.Open
        };

        return new ExchangeOrder(order.ClientOrderId, order.OrderId, state, order.QuantityFilled ?? 0m, state == ExchangeOrderState.Rejected ? order.RejectReason : null);
    }

    private static DateTimeOffset Utc(DateTime time)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
    }

    private sealed class Subscription(BybitSocketClient client) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await client.UnsubscribeAllAsync();
            client.Dispose();
        }
    }
}