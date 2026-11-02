using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>
///     One exchange account in memory for the live trading runtime: its orders, fills and holding, its streams and its answers.
///     A test fills orders, closes candles and drops the streams; the exchange keeps one order per client id, as Bybit does.
/// </summary>
public sealed class FakeExchange : IExchangeLedger, IOrderGateway, IExchangeStreams
{
    private readonly Lock _lock = new Lock();
    private readonly Dictionary<string, ExchangeOrder> _orders = [];
    private readonly Dictionary<string, OrderRequest> _requests = [];
    private readonly Dictionary<string, List<ExchangeFill>> _fills = [];
    private readonly Dictionary<string, List<ExchangeFill>> _late = [];
    private readonly List<Action<Candle>> _candles = [];
    private readonly List<Action<ExchangeOrder>> _orderUpdates = [];
    private readonly List<Action<ExchangeFill>> _fillUpdates = [];
    private readonly List<Action> _restored = [];
    private int _execs;
    private Action? _onHoldingRead;

    public decimal Free { get; set; } = 10_000m;

    /// <summary>The coin on the account, or the position of the symbol in <c>linear</c>.</summary>
    public decimal Holding { get; set; }

    /// <summary>Every order sent, a repeated one too.</summary>
    public List<OrderRequest> Sent { get; } = [];

    /// <summary>Every client id a cancel was sent for.</summary>
    public List<string> Cancels { get; } = [];

    /// <summary>When each cancel came, by the clock of the app.</summary>
    public List<(string ClientId, DateTimeOffset At)> CancelTimes { get; } = [];

    /// <summary>The clock of the app, which <see cref="CancelTimes" /> are read on.</summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>The answers to the next orders sent, before the exchange takes any.</summary>
    public Queue<OrderOutcome> Refusals { get; } = new Queue<OrderOutcome>();

    /// <summary>Holds every order sent until the test lets it through.</summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>Takes every order sent, then holds its answer until the test lets it through: the answer is lost when the Worker stops.</summary>
    public TaskCompletionSource? HoldAnswer { get; set; }

    /// <summary>The answers to the next cancels sent.</summary>
    public Queue<OrderOutcome> CancelRefusals { get; } = new Queue<OrderOutcome>();

    public IReadOnlyList<OrderRequest> Orders
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests.Values];
            }
        }
    }

    public int Subscribers
    {
        get
        {
            lock (_lock)
            {
                return _candles.Count;
            }
        }
    }

    public ExchangeOrder? Order(string clientId)
    {
        lock (_lock)
        {
            return _orders.GetValueOrDefault(clientId);
        }
    }

    /// <summary>Puts an order on the account as if someone placed it, without a stream event.</summary>
    public void Place(string clientId, OrderSide side, decimal quantity)
    {
        lock (_lock)
        {
            _requests[clientId] = new OrderRequest(clientId, side, OrderType.Limit, 100m, quantity, null);
            _orders[clientId] = new ExchangeOrder(clientId, $"x-{clientId}", ExchangeOrderState.Open, 0m, null);
        }
    }

    /// <summary>Fills an order and tells the streams; <paramref name="silently" /> keeps it from them, as a dropped connection does.</summary>
    public void Fill(string clientId, decimal quantity, decimal price, decimal fee = 0m, string feeCoin = "USDT", bool silently = false, string? execId = null)
    {
        ExchangeFill fill;
        ExchangeOrder order;
        Action<ExchangeFill>[] fills;
        Action<ExchangeOrder>[] orders;

        lock (_lock)
        {
            OrderRequest request = _requests[clientId];
            ExchangeOrder before = _orders[clientId];
            decimal filled = before.FilledQuantity + quantity;
            fill = new ExchangeFill(execId ?? $"e-{++_execs}", clientId, quantity, price, fee, feeCoin, DateTimeOffset.Parse("2024-03-10T12:30:00Z"));
            order = before with { FilledQuantity = filled, State = filled >= (request.Quantity ?? filled) ? ExchangeOrderState.Filled : ExchangeOrderState.Open };
            _orders[clientId] = order;
            _fills.TryAdd(clientId, []);
            _fills[clientId].Add(fill);
            Holding += request.Side == OrderSide.Buy ? quantity - (feeCoin == "USDT" ? 0m : fee) : -quantity;
            fills = silently ? [] : [.. _fillUpdates];
            orders = silently ? [] : [.. _orderUpdates];
        }

        foreach (Action<ExchangeFill> handler in fills)
        {
            handler(fill);
        }

        foreach (Action<ExchangeOrder> handler in orders)
        {
            handler(order);
        }
    }

    /// <summary>
    ///     Fills an order as Bybit often reports it: the order update with its filled quantity comes first, while the fill is
    ///     neither on the stream nor in the trade history yet. <see cref="Deliver" /> lets it out.
    /// </summary>
    public void FillLate(string clientId, decimal quantity, decimal price)
    {
        ExchangeOrder order;
        Action<ExchangeOrder>[] orders;

        lock (_lock)
        {
            OrderRequest request = _requests[clientId];
            ExchangeOrder before = _orders[clientId];
            decimal filled = before.FilledQuantity + quantity;
            order = before with { FilledQuantity = filled, State = filled >= (request.Quantity ?? filled) ? ExchangeOrderState.Filled : ExchangeOrderState.Open };
            _orders[clientId] = order;
            _late.TryAdd(clientId, []);
            _late[clientId].Add(new ExchangeFill($"e-{++_execs}", clientId, quantity, price, 0m, "USDT", DateTimeOffset.Parse("2024-03-10T12:30:00Z")));
            Holding += request.Side == OrderSide.Buy ? quantity : -quantity;
            orders = [.. _orderUpdates];
        }

        foreach (Action<ExchangeOrder> handler in orders)
        {
            handler(order);
        }
    }

    /// <summary>The late fills of an order reach the trade history and, unless <paramref name="silently" />, the stream.</summary>
    public void Deliver(string clientId, bool silently = false)
    {
        ExchangeFill[] late;
        Action<ExchangeFill>[] handlers;

        lock (_lock)
        {
            late = [.. _late.GetValueOrDefault(clientId) ?? []];
            _late.Remove(clientId);
            _fills.TryAdd(clientId, []);
            _fills[clientId].AddRange(late);
            handlers = silently ? [] : [.. _fillUpdates];
        }

        foreach (ExchangeFill fill in late)
        {
            foreach (Action<ExchangeFill> handler in handlers)
            {
                handler(fill);
            }
        }
    }

    /// <summary>Repeats a fill on the stream, as Bybit may after a reconnect.</summary>
    public void Repeat(string clientId)
    {
        ExchangeFill fill;
        Action<ExchangeFill>[] handlers;

        lock (_lock)
        {
            fill = _fills[clientId][^1];
            handlers = [.. _fillUpdates];
        }

        foreach (Action<ExchangeFill> handler in handlers)
        {
            handler(fill);
        }
    }

    /// <summary>A fill of an order nobody of the system placed, such as the owner by hand.</summary>
    public void ForeignFill(string clientId, decimal quantity, decimal price)
    {
        Action<ExchangeFill>[] handlers;

        lock (_lock)
        {
            handlers = [.. _fillUpdates];
        }

        foreach (Action<ExchangeFill> handler in handlers)
        {
            handler(new ExchangeFill($"e-{Interlocked.Increment(ref _execs)}", clientId, quantity, price, 0m, "USDT", DateTimeOffset.Parse("2024-03-10T12:30:00Z")));
        }
    }

    public void CloseCandle(DateTimeOffset openTime, decimal close)
    {
        Action<Candle>[] handlers;

        lock (_lock)
        {
            handlers = [.. _candles];
        }

        foreach (Action<Candle> handler in handlers)
        {
            handler(new Candle(openTime, new Price(close), new Price(close), new Price(close), new Price(close), new Volume(1m), new Volume(close)));
        }
    }

    /// <summary>The streams come back after a gap.</summary>
    public void Restore()
    {
        Action[] handlers;

        lock (_lock)
        {
            handlers = [.. _restored];
        }

        foreach (Action handler in handlers)
        {
            handler();
        }
    }

    public Task<decimal> GetFreeAsync(ExchangeKey key, string coin, CancellationToken cancellationToken)
    {
        return Task.FromResult(Free);
    }

    public Task<bool> IsHedgeModeAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public Task<decimal> GetHoldingAsync(ExchangeKey key, Instrument instrument, string baseCoin, CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _onHoldingRead, null)?.Invoke();

        return Task.FromResult(Holding);
    }

    /// <summary>Runs once, the next time the holding is read: something happens on the exchange right then.</summary>
    public void OnHoldingRead(Action action)
    {
        _onHoldingRead = action;
    }

    public Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<ExchangeOrder>>([.. _orders.Values.Where(order => order.State == ExchangeOrderState.Open)]);
        }
    }

    public Task<ExchangeOrder?> FindOrderAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Order(clientId));
    }

    public Task<IReadOnlyList<ExchangeFill>> GetFillsAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<ExchangeFill>>([.. _fills.GetValueOrDefault(clientId) ?? []]);
        }
    }

    public Task<FeeSchedule> GetFeesAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken)
    {
        return Task.FromResult(new FeeSchedule(0m, 0m, instrument.Category));
    }

    public async Task<OrderOutcome> PlaceAsync(ExchangeKey key, Instrument instrument, OrderRequest order, CancellationToken cancellationToken)
    {
        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        lock (_lock)
        {
            Sent.Add(order);

            if (Refusals.TryDequeue(out OrderOutcome? refusal))
            {
                return refusal;
            }

            if (_orders.TryGetValue(order.ClientId, out ExchangeOrder? taken))
            {
                return new OrderOutcome(OrderOutcomeKind.Accepted, taken.ExchangeId);
            }

            _requests[order.ClientId] = order;
            _orders[order.ClientId] = new ExchangeOrder(order.ClientId, $"x-{order.ClientId}", ExchangeOrderState.Open, 0m, null);
        }

        if (HoldAnswer is { } answer)
        {
            await answer.Task.WaitAsync(cancellationToken);
        }

        return new OrderOutcome(OrderOutcomeKind.Accepted, $"x-{order.ClientId}");
    }

    /// <summary>The exchange refuses an order it took, as Bybit may on the stream.</summary>
    public void Reject(string clientId, string reason)
    {
        ExchangeOrder rejected;
        Action<ExchangeOrder>[] handlers;

        lock (_lock)
        {
            rejected = _orders[clientId] with { State = ExchangeOrderState.Rejected, RejectReason = reason };
            _orders[clientId] = rejected;
            handlers = [.. _orderUpdates];
        }

        foreach (Action<ExchangeOrder> handler in handlers)
        {
            handler(rejected);
        }
    }

    /// <summary>The order is done on the exchange with what it got, and the streams are told.</summary>
    public void Finish(string clientId)
    {
        ExchangeOrder finished;
        Action<ExchangeOrder>[] handlers;

        lock (_lock)
        {
            finished = _orders[clientId] with { State = ExchangeOrderState.Filled };
            _orders[clientId] = finished;
            handlers = [.. _orderUpdates];
        }

        foreach (Action<ExchangeOrder> handler in handlers)
        {
            handler(finished);
        }
    }

    /// <summary>A cancel takes the order off at once and tells the streams, as Bybit confirms it.</summary>
    public Task<OrderOutcome> CancelAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken)
    {
        ExchangeOrder cancelled;
        Action<ExchangeOrder>[] handlers;

        lock (_lock)
        {
            Cancels.Add(clientId);
            CancelTimes.Add((clientId, Clock.GetUtcNow()));

            if (CancelRefusals.TryDequeue(out OrderOutcome? refusal))
            {
                return Task.FromResult(refusal);
            }

            if (!_orders.TryGetValue(clientId, out ExchangeOrder? order) || order.State != ExchangeOrderState.Open)
            {
                return Task.FromResult(new OrderOutcome(OrderOutcomeKind.AlreadyDone));
            }

            cancelled = order with { State = ExchangeOrderState.Cancelled };
            _orders[clientId] = cancelled;
            handlers = [.. _orderUpdates];
        }

        foreach (Action<ExchangeOrder> handler in handlers)
        {
            handler(cancelled);
        }

        return Task.FromResult(new OrderOutcome(OrderOutcomeKind.Accepted, cancelled.ExchangeId));
    }

    public Task<IAsyncDisposable> SubscribeCandlesAsync(Instrument instrument, Interval interval, Action<Candle> closed, Action restored, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _candles.Add(closed);
            _restored.Add(restored);
        }

        return Task.FromResult<IAsyncDisposable>(new Unsubscribe(() =>
        {
            lock (_lock)
            {
                _candles.Remove(closed);
                _restored.Remove(restored);
            }
        }));
    }

    public Task<IAsyncDisposable> SubscribeAccountAsync(ExchangeKey key, Action<ExchangeOrder> order, Action<ExchangeFill> fill, Action restored, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _orderUpdates.Add(order);
            _fillUpdates.Add(fill);
        }

        return Task.FromResult<IAsyncDisposable>(new Unsubscribe(() =>
        {
            lock (_lock)
            {
                _orderUpdates.Remove(order);
                _fillUpdates.Remove(fill);
            }
        }));
    }

    private sealed class Unsubscribe(Action action) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            action();

            return ValueTask.CompletedTask;
        }
    }
}