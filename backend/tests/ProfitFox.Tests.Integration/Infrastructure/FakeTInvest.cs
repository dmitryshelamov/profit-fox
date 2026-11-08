using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using ProfitFox.Infrastructure.Exchanges.TInvest;
using ProfitFox.Tests.Shared.TInvest;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>
///     One brokerage account of T-Invest with the share SBER (lot 10, step 0.01) on the handlers and scripts of <see cref="MockTInvestApi" />:
///     its orders by request id, its shares and roubles, the trading status by the schedule (the main and the evening sessions of a day,
///     the morning of the next one), the history of hourly candles at 300, and the streams of orders and candles. A test fills, cancels and
///     forgets orders and closes candles; no call leaves the process.
/// </summary>
public sealed class FakeTInvest
{
    public const string AccountNumber = "2000000001";
    public const string SberUid = "e6123145-9665-43e0-8413-cd61b8aa9b13";
    public const string Exchange = "moex_mrng_evng_e_wknd_dlr";
    public const int Lot = 10;

    private readonly Lock _lock = new Lock();
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, Order> _orders = [];
    private readonly List<(PostOrderRequest Request, DateTimeOffset At)> _posted = [];

    /// <param name="api">The mock the app calls.</param>
    /// <param name="clock">The clock of the app.</param>
    /// <param name="sessions">The intervals of the exchange by the schedule, each a session open between its start and end.</param>
    public FakeTInvest(MockTInvestApi api, TimeProvider clock, IReadOnlyList<(string Type, DateTimeOffset Start, DateTimeOffset End)> sessions)
    {
        _clock = clock;
        Sessions = sessions;
        OrderStream = api.OnServerStream<OrderStateStreamRequest, OrderStateStreamResponse>("OrderStateStream");
        MarketStream = api.OnDuplex<MarketDataRequest, MarketDataResponse>("MarketDataStream");
        api.On<InstrumentRequest, ShareResponse>("ShareBy", _ => new ShareResponse { Instrument = Sber() })
            .On<InstrumentsRequest, SharesResponse>("Shares", _ => new SharesResponse { Instruments = { Sber() } })
            .On<GetCandlesRequest, GetCandlesResponse>("GetCandles", Candles)
            .On<GetLastPricesRequest, GetLastPricesResponse>("GetLastPrices", _ => new GetLastPricesResponse { LastPrices = { new LastPrice { InstrumentUid = SberUid, Price = 300m.ToQuotation() } } })
            .On<GetOrderPriceRequest, GetOrderPriceResponse>("GetOrderPrice", _ => new GetOrderPriceResponse { InitialOrderAmount = Money(100_000m), DealCommission = Money(50m) })
            .On<PositionsRequest, PositionsResponse>("GetPositions", _ => Positions())
            .On<GetTradingStatusRequest, GetTradingStatusResponse>("GetTradingStatus", _ => Status())
            .On<GetOrderBookRequest, GetOrderBookResponse>("GetOrderBook", _ => new GetOrderBookResponse { LimitDown = 200m.ToQuotation(), LimitUp = 400m.ToQuotation() })
            .On<TradingSchedulesRequest, TradingSchedulesResponse>("TradingSchedules", _ => Schedule())
            .On<PostOrderRequest, PostOrderResponse>("PostOrder", Post)
            .On<GetOrderStateRequest, OrderState>("GetOrderState", StateOf)
            .On<GetOrdersRequest, GetOrdersResponse>("GetOrders", _ => Active())
            .On<CancelOrderRequest, CancelOrderResponse>("CancelOrder", Cancel);
    }

    public IReadOnlyList<(string Type, DateTimeOffset Start, DateTimeOffset End)> Sessions { get; }

    public MockTInvestStream<OrderStateStreamRequest, OrderStateStreamResponse> OrderStream { get; }

    public MockTInvestStream<MarketDataRequest, MarketDataResponse> MarketStream { get; }

    /// <summary>Every order sent and when, by the clock of the app.</summary>
    public IReadOnlyList<(PostOrderRequest Request, DateTimeOffset At)> Posted
    {
        get
        {
            lock (_lock)
            {
                return [.. _posted];
            }
        }
    }

    /// <summary>Whether the exchange takes orders now, by the schedule.</summary>
    public bool Open => Sessions.Any(session => session.Start <= _clock.GetUtcNow() && _clock.GetUtcNow() < session.End);

    /// <summary>
    ///     Fills the order of the request whole at the price and tells the stream; its commission is <paramref name="commission" /> at once,
    ///     or <paramref name="late" /> once the clock is <paramref name="after" /> past the fill.
    /// </summary>
    public void Fill(string request, decimal price, decimal commission, decimal late = 0m, TimeSpan? after = null)
    {
        DateTimeOffset now = _clock.GetUtcNow();

        lock (_lock)
        {
            Order order = _orders[request];
            order.Status = OrderExecutionReportStatus.ExecutionReportStatusFill;
            order.Executed = order.Request.Quantity;
            order.Price = price;
            order.Commission = commission;
            order.Late = (late, after is { } delay ? now + delay : null);
            order.FilledAt = now;
        }

        Tell(request);
    }

    /// <summary>Takes the active orders of the day off as the exchange does at the end of a trading day and tells the stream.</summary>
    public void ExpireDayOrders()
    {
        string[] expired;

        lock (_lock)
        {
            expired = [.. _orders.Where(order => order.Value.Active && order.Value.Request.TimeInForce == TimeInForceType.TimeInForceDay).Select(order => order.Key)];

            foreach (string request in expired)
            {
                _orders[request].Status = OrderExecutionReportStatus.ExecutionReportStatusCancelled;
            }
        }

        foreach (string request in expired)
        {
            Tell(request);
        }
    }

    /// <summary>The exchange no longer knows the order, as it does not know an order of a day gone.</summary>
    public void Forget(string request)
    {
        lock (_lock)
        {
            _orders.Remove(request);
        }
    }

    /// <summary>The request ids of the orders sent as limit orders, the oldest first.</summary>
    public IReadOnlyList<string> Limits => [.. Posted.Where(post => post.Request.OrderType == OrderType.Limit).Select(post => post.Request.OrderId)];

    /// <summary>Tells the stream of orders that the order of the request changed; the stream carries no state of its own.</summary>
    public void Tell(string request)
    {
        OrderStream.Send(new OrderStateStreamResponse
        {
            OrderState = new OrderStateStreamResponse.Types.OrderState { OrderId = $"R-{request[..8]}", OrderRequestId = request, Ticker = "SBER", LotSize = Lot, AccountId = AccountNumber }
        });
    }

    /// <summary>Pings both streams, as the exchange does every 30 s.</summary>
    public void Ping()
    {
        if (OrderStream.IsOpen)
        {
            OrderStream.Send(new OrderStateStreamResponse { Ping = new Tinkoff.InvestApi.V1.Ping() });
        }

        foreach (MockTInvestStreamCall<MarketDataRequest, MarketDataResponse> stream in MarketStream.Streams.Where(stream => stream.IsOpen))
        {
            stream.Send(new MarketDataResponse { Ping = new Tinkoff.InvestApi.V1.Ping() });
        }
    }

    /// <summary>Closes the hourly candle that opened at <paramref name="open" /> on the stream of closed candles.</summary>
    public void CloseCandle(DateTimeOffset open, decimal close)
    {
        MockTInvestStreamCall<MarketDataRequest, MarketDataResponse> closed = MarketStream.Streams.Last(stream => stream.Requests.Any(request => request.SubscribeCandlesRequest is { WaitingClose: true }));
        closed.Send(new MarketDataResponse
        {
            Candle = new Tinkoff.InvestApi.V1.Candle
            {
                InstrumentUid = SberUid,
                Interval = SubscriptionInterval.OneHour,
                Time = Timestamp.FromDateTimeOffset(open),
                Open = close.ToQuotation(),
                High = close.ToQuotation(),
                Low = close.ToQuotation(),
                Close = close.ToQuotation(),
                Volume = 1
            }
        });
    }

    /// <summary>Whether the stream of closed candles has the hourly candles of SBER.</summary>
    public bool ClosedCandlesSubscribed => MarketStream.Streams.Any(stream => stream.IsOpen && stream.Requests.Any(request => request.SubscribeCandlesRequest is { WaitingClose: true, SubscriptionAction: SubscriptionAction.Subscribe }));

    public static Share Sber()
    {
        return new Share
        {
            Uid = SberUid,
            Ticker = "SBER",
            ClassCode = "TQBR",
            Name = "Сбер Банк",
            Currency = "rub",
            Lot = Lot,
            MinPriceIncrement = 0.01m.ToQuotation(),
            ApiTradeAvailableFlag = true,
            Exchange = Exchange
        };
    }

    private GetCandlesResponse Candles(GetCandlesRequest request)
    {
        var response = new GetCandlesResponse();
        DateTimeOffset now = _clock.GetUtcNow();
        DateTimeOffset from = request.From.ToUtcMoment();
        var start = new DateTimeOffset(from.Year, from.Month, from.Day, from.Hour, 0, 0, TimeSpan.Zero);

        for (DateTimeOffset open = start; open < request.To.ToUtcMoment(); open = open.AddHours(1))
        {
            if (open >= from)
            {
                response.Candles.Add(new HistoricCandle
                {
                    Time = Timestamp.FromDateTimeOffset(open),
                    Open = 300m.ToQuotation(),
                    High = 300m.ToQuotation(),
                    Low = 300m.ToQuotation(),
                    Close = 300m.ToQuotation(),
                    Volume = 1,
                    IsComplete = open.AddHours(1) <= now
                });
            }
        }

        return response;
    }

    private PositionsResponse Positions()
    {
        lock (_lock)
        {
            long lots = _orders.Values.Sum(order => order.Request.Direction == OrderDirection.Buy ? order.Executed : -order.Executed);

            return new PositionsResponse
            {
                Money = { Money(1_000_000m) },
                Securities = { new PositionsSecurities { InstrumentUid = SberUid, Ticker = "SBER", Balance = lots * Lot } }
            };
        }
    }

    private GetTradingStatusResponse Status()
    {
        bool open = Open;

        return new GetTradingStatusResponse { InstrumentUid = SberUid, ApiTradeAvailableFlag = true, LimitOrderAvailableFlag = open, MarketOrderAvailableFlag = open };
    }

    private TradingSchedulesResponse Schedule()
    {
        var schedule = new TradingSchedule { Exchange = Exchange };

        foreach (IGrouping<DateTime, (string Type, DateTimeOffset Start, DateTimeOffset End)> day in Sessions.GroupBy(session => session.Start.UtcDateTime.Date))
        {
            schedule.Days.Add(new TradingDay
            {
                Date = Timestamp.FromDateTimeOffset(new DateTimeOffset(day.Key, TimeSpan.Zero)),
                IsTradingDay = true,
                Intervals =
                {
                    day.Select(session => new TradingInterval
                    {
                        Type = session.Type,
                        Interval = new TradingInterval.Types.TimeInterval { StartTs = Timestamp.FromDateTimeOffset(session.Start), EndTs = Timestamp.FromDateTimeOffset(session.End) }
                    })
                }
            });
        }

        return new TradingSchedulesResponse { Exchanges = { schedule } };
    }

    private PostOrderResponse Post(PostOrderRequest request)
    {
        lock (_lock)
        {
            _posted.Add((request, _clock.GetUtcNow()));

            if (!_orders.ContainsKey(request.OrderId))
            {
                _orders[request.OrderId] = new Order(request);
            }

            return new PostOrderResponse { OrderId = $"R-{request.OrderId[..8]}", OrderRequestId = request.OrderId, ExecutionReportStatus = _orders[request.OrderId].Status };
        }
    }

    private OrderState StateOf(GetOrderStateRequest request)
    {
        lock (_lock)
        {
            Order? order = request.OrderIdType == OrderIdType.Request
                ? _orders.GetValueOrDefault(request.OrderId)
                : _orders.Values.FirstOrDefault(item => $"R-{item.Request.OrderId[..8]}" == request.OrderId);

            return order?.State(_clock.GetUtcNow()) ?? throw new RpcException(new Status(StatusCode.NotFound, "50005"));
        }
    }

    private GetOrdersResponse Active()
    {
        lock (_lock)
        {
            DateTimeOffset now = _clock.GetUtcNow();

            return new GetOrdersResponse { Orders = { _orders.Values.Where(order => order.Active).Select(order => order.State(now)) } };
        }
    }

    private CancelOrderResponse Cancel(CancelOrderRequest request)
    {
        lock (_lock)
        {
            if (!_orders.TryGetValue(request.OrderId, out Order? order) || !order.Active)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "50005"));
            }

            order.Status = OrderExecutionReportStatus.ExecutionReportStatusCancelled;
        }

        Tell(request.OrderId);

        return new CancelOrderResponse();
    }

    private static MoneyValue Money(decimal value)
    {
        var quotation = value.ToQuotation();

        return new MoneyValue { Currency = "rub", Units = quotation.Units, Nano = quotation.Nano };
    }

    private sealed class Order(PostOrderRequest request)
    {
        public PostOrderRequest Request => request;

        public OrderExecutionReportStatus Status { get; set; } = OrderExecutionReportStatus.ExecutionReportStatusNew;

        public long Executed { get; set; }

        public decimal Price { get; set; }

        public decimal Commission { get; set; }

        public (decimal Commission, DateTimeOffset? From) Late { get; set; }

        public DateTimeOffset FilledAt { get; set; }

        public bool Active => Status is OrderExecutionReportStatus.ExecutionReportStatusNew or OrderExecutionReportStatus.ExecutionReportStatusPartiallyfill;

        public OrderState State(DateTimeOffset now)
        {
            decimal commission = Late.From is { } from && now >= from ? Late.Commission : Commission;
            var state = new OrderState
            {
                OrderId = $"R-{request.OrderId[..8]}",
                OrderRequestId = request.OrderId,
                InstrumentUid = SberUid,
                Ticker = "SBER",
                ExecutionReportStatus = Status,
                LotsRequested = request.Quantity,
                LotsExecuted = Executed,
                Direction = request.Direction,
                OrderType = request.OrderType,
                ExecutedCommission = Money(commission)
            };

            if (Executed > 0)
            {
                state.Stages.Add(new OrderStage { TradeId = $"T-{request.OrderId[..8]}", Price = Money(Price), Quantity = Executed, ExecutionTime = Timestamp.FromDateTimeOffset(FilledAt) });
            }

            return state;
        }
    }
}