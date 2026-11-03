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
///     Bybit v5 streams shared by every subscriber of the process: one public socket client, whose connections carry one kline
///     topic per instrument and interval however many subscribers listen to it, and the private order and execution topics of
///     an account. CryptoExchange.Net reconnects; a restored connection is told to every subscriber of its topics.
/// </summary>
internal sealed class BybitStreams(ILoggerFactory loggers, BybitSettings settings) : IExchangeStreams
{
    private readonly BybitSocketClient _public = new BybitSocketClient(Options.Create(new BybitSocketOptions { Environment = settings.Environment() }), loggers);
    private readonly Lock _lock = new Lock();

    // A topic stays here once made; only its subscription comes and goes, under the gate of the topic.
    private readonly Dictionary<(MarketCategory, string, Interval), Topic<CandleListener>> _candles = [];
    private readonly Dictionary<string, Topic<AccountListener>> _accounts = [];

    public Task<IAsyncDisposable> SubscribeCandlesAsync(Instrument instrument, Interval interval, Action<Candle> closed, Action restored, CancellationToken cancellationToken)
    {
        Topic<CandleListener> topic;

        lock (_lock)
        {
            (MarketCategory, string, Interval) key = (instrument.Category, instrument.Symbol, interval);
            topic = _candles.TryGetValue(key, out Topic<CandleListener>? known) ? known : _candles[key] = new Topic<CandleListener>();
        }

        return JoinAsync(topic, new CandleListener(closed, restored), token => OpenCandlesAsync(instrument, interval, topic, token), cancellationToken);
    }

    public Task<IAsyncDisposable> SubscribeAccountAsync(
        ExchangeKey key,
        Action<ExchangeOrder> order,
        Action<ExchangeFill> fill,
        Action restored,
        CancellationToken cancellationToken)
    {
        Topic<AccountListener> topic;

        lock (_lock)
        {
            topic = _accounts.TryGetValue(key.ApiKey, out Topic<AccountListener>? known) ? known : _accounts[key.ApiKey] = new Topic<AccountListener>();
        }

        return JoinAsync(topic, new AccountListener(order, fill, restored), token => OpenAccountAsync(key, topic, token), cancellationToken);
    }

    /// <summary>The first subscriber opens the subscription of the topic; the others only join it.</summary>
    private static async Task<IAsyncDisposable> JoinAsync<T>(Topic<T> topic, T listener, Func<CancellationToken, Task<IAsyncDisposable>> open, CancellationToken cancellationToken)
        where T : class, IListener
    {
        await topic.Gate.WaitAsync(cancellationToken);

        try
        {
            topic.Subscription ??= await open(cancellationToken);
            topic.Add(listener);
        }
        finally
        {
            topic.Gate.Release();
        }

        return new Leave(() => LeaveAsync(topic, listener));
    }

    /// <summary>The last subscriber to leave takes the subscription of the topic off.</summary>
    private static async ValueTask LeaveAsync<T>(Topic<T> topic, T listener)
        where T : class, IListener
    {
        await topic.Gate.WaitAsync();

        try
        {
            topic.Remove(listener);

            if (topic.Listeners.Count == 0 && topic.Subscription is { } subscription)
            {
                topic.Subscription = null;
                await subscription.DisposeAsync();
            }
        }
        finally
        {
            topic.Gate.Release();
        }
    }

    /// <summary>A kline topic on the shared public client; a connection with no topic left is closed by the client.</summary>
    private async Task<IAsyncDisposable> OpenCandlesAsync(Instrument instrument, Interval interval, Topic<CandleListener> topic, CancellationToken cancellationToken)
    {
        WebSocketResult<UpdateSubscription> result = instrument.Category == MarketCategory.Spot
            ? await _public.V5SpotApi.SubscribeToKlineUpdatesAsync(instrument.Symbol, interval.ToBybit(), update => Closed(update.Data, topic), cancellationToken)
            : await _public.V5LinearApi.SubscribeToKlineUpdatesAsync(instrument.Symbol, interval.ToBybit(), update => Closed(update.Data, topic), cancellationToken);
        UpdateSubscription subscription = Watch(result, topic);

        return new Leave(() => new ValueTask(_public.UnsubscribeAsync(subscription)));
    }

    /// <summary>The order and execution topics of an account on a client of its own key; leaving closes the client.</summary>
    private async Task<IAsyncDisposable> OpenAccountAsync(ExchangeKey key, Topic<AccountListener> topic, CancellationToken cancellationToken)
    {
        var client = new BybitSocketClient(
            Options.Create(new BybitSocketOptions { Environment = settings.Environment(), ApiCredentials = new BybitCredentials(key.ApiKey, key.Secret) }),
            loggers);
        var stream = new Subscription(client);

        try
        {
            Watch(
                await client.V5PrivateApi.SubscribeToOrderUpdatesAsync(
                    update =>
                    {
                        foreach (BybitOrderUpdate item in update.Data)
                        {
                            ExchangeOrder order = Order(item);

                            foreach (AccountListener listener in topic.Listeners)
                            {
                                listener.Order(order);
                            }
                        }
                    },
                    cancellationToken),
                topic);
            Watch(
                await client.V5PrivateApi.SubscribeToUserTradeUpdatesAsync(
                    update =>
                    {
                        foreach (BybitUserTradeUpdate trade in update.Data)
                        {
                            var fill = new ExchangeFill(trade.TradeId, trade.ClientOrderId ?? "", trade.Quantity, trade.Price, trade.Fee ?? 0m, trade.FeeAsset ?? "", Utc(trade.Timestamp));

                            foreach (AccountListener listener in topic.Listeners)
                            {
                                listener.Fill(fill);
                            }
                        }
                    },
                    cancellationToken),
                topic);
        }
        catch
        {
            await stream.DisposeAsync();

            throw;
        }

        return stream;
    }

    /// <summary>A subscription Bybit took, whose restored connection is told to every subscriber of the topic.</summary>
    private static UpdateSubscription Watch<T>(WebSocketResult<UpdateSubscription> result, Topic<T> topic)
        where T : class, IListener
    {
        if (!result.Success)
        {
            throw new ExchangeUnavailableException($"Bybit refused the stream: {result.Error!.Message}");
        }

        result.Data.ConnectionRestored += _ =>
        {
            foreach (T listener in topic.Listeners)
            {
                listener.Restored();
            }
        };

        return result.Data;
    }

    private static void Closed(IEnumerable<BybitKlineUpdate> klines, Topic<CandleListener> topic)
    {
        foreach (BybitKlineUpdate kline in klines.Where(kline => kline.Confirm))
        {
            var candle = new Candle(
                Utc(kline.StartTime),
                new Price(kline.OpenPrice),
                new Price(kline.HighPrice),
                new Price(kline.LowPrice),
                new Price(kline.ClosePrice),
                new Volume(kline.Volume),
                new Volume(kline.Turnover));

            foreach (CandleListener listener in topic.Listeners)
            {
                listener.Closed(candle);
            }
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

        return new ExchangeOrder(
            order.ClientOrderId ?? "",
            order.OrderId,
            state,
            order.QuantityFilled ?? 0m,
            state == ExchangeOrderState.Rejected ? order.RejectReason : null,
            order.ValueFilled ?? 0m,
            order.ExecutedFee ?? 0m,
            order.FeeAsset ?? "");
    }

    private static DateTimeOffset Utc(DateTime time)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
    }

    private interface IListener
    {
        Action Restored { get; }
    }

    private sealed record CandleListener(Action<Candle> Closed, Action Restored) : IListener;

    private sealed record AccountListener(Action<ExchangeOrder> Order, Action<ExchangeFill> Fill, Action Restored) : IListener;

    /// <summary>
    ///     The subscribers of one topic. They come and go under <see cref="Gate" />; the stream reads them meanwhile, so each
    ///     change makes a new array.
    /// </summary>
    private sealed class Topic<T>
        where T : class
    {
        private T[] _listeners = [];

        public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1, 1);

        public IAsyncDisposable? Subscription { get; set; }

        public IReadOnlyList<T> Listeners => Volatile.Read(ref _listeners);

        public void Add(T listener)
        {
            Volatile.Write(ref _listeners, [.. _listeners, listener]);
        }

        public void Remove(T listener)
        {
            Volatile.Write(ref _listeners, [.. _listeners.Where(item => !ReferenceEquals(item, listener))]);
        }
    }

    private sealed class Leave(Func<ValueTask> leave) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return leave();
        }
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