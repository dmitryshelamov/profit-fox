using System.Collections.Concurrent;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Microsoft.Extensions.Diagnostics.Metrics.Testing;

using NSubstitute;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Infrastructure.Exchanges.TInvest;
using ProfitFox.Tests.Shared.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

using Candle = ProfitFox.Domain.Entities.MarketData.Candle;
using ExchangeOrderState = ProfitFox.Application.Ports.LiveTrading.ExchangeOrderState;
using Instrument = ProfitFox.Domain.ValueObjects.MarketData.Instrument;
using StreamOrderState = Tinkoff.InvestApi.V1.OrderStateStreamResponse.Types.OrderState;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Decisions 7 and 9: the streams of T-Invest of the process on the scripts of <see cref="MockTInvestApi" />.</summary>
public class TInvestStreamsTests
{
    private const string AccountNumber = "2000000001";
    private const string GazpUid = "962e2a95-02a9-4171-abd7-aa198dbe643a";
    private static readonly ExchangeKey Key = new ExchangeKey(AccountNumber, TInvestTestHost.Token, Exchange.TInvest);
    private static readonly Instrument Sber = new Instrument(MarketCategory.Moex, "SBER");
    private static readonly Instrument Gazp = new Instrument(MarketCategory.Moex, "GAZP");
    private static readonly Guid Request = Guid.Parse("6f1c2c1e-6d0e-4b8b-9f43-2f6a0c1b2d3e");

    private TInvestTestHost _host = null!;
    private ITInvestOrderIds _ids = null!;
    private TInvestStreams _streams = null!;
    private MockTInvestStream<OrderStateStreamRequest, OrderStateStreamResponse> _orders = null!;
    private MockTInvestStream<MarketDataRequest, MarketDataResponse> _market = null!;
    private readonly List<IAsyncDisposable> _subscriptions = [];

    [SetUp]
    public void SetUp()
    {
        _host = new TInvestTestHost();
        _host.Time.SetUtcNow(DateTimeOffset.Parse("2024-03-11T10:00:00Z"));
        _ids = Substitute.For<ITInvestOrderIds>();
        _ids.ClientIdOfAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        _ids.ClientIdOfAsync(Request, Arg.Any<CancellationToken>()).Returns("b-1");
        _orders = _host.Api.OnServerStream<OrderStateStreamRequest, OrderStateStreamResponse>("OrderStateStream");
        _market = _host.Api.OnDuplex<MarketDataRequest, MarketDataResponse>("MarketDataStream");
        _host.Api
            .On<InstrumentRequest, ShareResponse>("ShareBy", request => new ShareResponse { Instrument = request.Id == "GAZP" ? Gazp10() : TInvestFixtures.Sber() })
            .Fail("TradingSchedules", StatusCode.Unavailable);
        _streams = new TInvestStreams(_host.Invokers, _host.Settings, _host.Metrics, _host.Call, _host.Tokens, _host.Shares, _ids, new TInvestTradingStatuses(_host.Call, _host.Time), _host.Time);
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (IAsyncDisposable subscription in _subscriptions)
        {
            await subscription.DisposeAsync();
        }

        _host.Dispose();
    }

    // Scenario "Два бота на одном аккаунте": one stream of the orders of the account, with pings, with the token of the key.
    [Test]
    public async Task Listeners_of_one_account_share_one_stream_of_its_orders()
    {
        await AccountAsync(new Events());
        await AccountAsync(new Events());

        await EventuallyAsync(() => _orders.IsOpen, "the stream of orders");
        _orders.Opens.ShouldBe(1);
        OrderStateStreamRequest request = _orders.Requests.Single();
        (request.Accounts.Single(), request.PingDelayMillis).ShouldBe((AccountNumber, 30_000));
        _host.Api.Calls.Single(call => call.Method == "OrderStateStream").Header("authorization").ShouldBe($"Bearer {TInvestTestHost.Token}");
    }

    [Test]
    public async Task Last_listener_to_leave_closes_the_stream_of_orders()
    {
        IAsyncDisposable first = await AccountAsync(new Events());
        IAsyncDisposable second = await AccountAsync(new Events());
        await EventuallyAsync(() => _orders.IsOpen, "the stream of orders");

        await first.DisposeAsync();
        _orders.IsOpen.ShouldBeTrue();
        await second.DisposeAsync();

        _orders.IsOpen.ShouldBeFalse();
    }

    // The message is a signal: the order of the bot comes from the state read by its request id.
    [Test]
    public async Task Order_of_a_bot_comes_from_its_state_read_by_the_request_id()
    {
        _host.Api.On<GetOrderStateRequest, OrderState>("GetOrderState", _ => State(OrderExecutionReportStatus.ExecutionReportStatusNew));
        var events = new Events();
        await OpenAccountAsync(events);

        _orders.Send(Message(Request.ToString()));

        await EventuallyAsync(() => !events.Orders.IsEmpty, "the order");
        events.Orders.Single().ShouldBe(new ExchangeOrder("b-1", "R-1", ExchangeOrderState.Open, 0m, null, 0m, 0m, "RUB"));
        var read = (GetOrderStateRequest)_host.Api.Calls.Single(call => call.Method == "GetOrderState").Request;
        (read.AccountId, read.OrderId, read.OrderIdType).ShouldBe((AccountNumber, Request.ToString(), OrderIdType.Request));
    }

    // Scenario "Событие раньше состояния": the state is read again a second later while T-Invest does not know the request.
    [Test]
    public async Task Order_told_before_its_state_knows_it_comes_once_the_state_does()
    {
        var reads = 0;
        _host.Api.On<GetOrderStateRequest, OrderState>("GetOrderState", _ => ++reads < 3 ? throw new RpcException(new Status(StatusCode.NotFound, "50005")) : State(OrderExecutionReportStatus.ExecutionReportStatusNew));
        var events = new Events();
        await OpenAccountAsync(events);
        DateTimeOffset told = _host.Time.GetUtcNow();

        _orders.Send(Message(Request.ToString()));

        await AdvanceUntilAsync(() => !events.Orders.IsEmpty, TimeSpan.FromMilliseconds(100), "the order");
        reads.ShouldBe(3);
        (_host.Time.GetUtcNow() - told).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Order_t_invest_never_knows_is_read_five_times_and_comes_not()
    {
        _host.Api.Fail("GetOrderState", StatusCode.NotFound, "50005");
        var events = new Events();
        await OpenAccountAsync(events);

        _orders.Send(Message(Request.ToString()));

        await AdvanceUntilAsync(() => _host.Api.Calls.Count(call => call.Method == "GetOrderState") == 5, TimeSpan.FromMilliseconds(100), "five reads");
        _host.Time.Advance(TimeSpan.FromSeconds(10));
        await Task.Delay(50);
        _host.Api.Calls.Count(call => call.Method == "GetOrderState").ShouldBe(5);
        events.Orders.ShouldBeEmpty();
    }

    // The two parts of one answer disagree (ProfitGraph): the state is read again.
    [Test]
    public async Task State_whose_trades_do_not_add_up_is_read_again()
    {
        var reads = 0;
        _host.Api.On<GetOrderStateRequest, OrderState>("GetOrderState", _ => ++reads == 1
            ? State(OrderExecutionReportStatus.ExecutionReportStatusPartiallyfill, executed: 2)
            : State(OrderExecutionReportStatus.ExecutionReportStatusPartiallyfill, executed: 2, stages: Stage("t-1", 250.5m, 2)));
        var events = new Events();
        await OpenAccountAsync(events);

        _orders.Send(Message(Request.ToString()));

        await AdvanceUntilAsync(() => !events.Orders.IsEmpty, TimeSpan.FromMilliseconds(100), "the order");
        events.Orders.Single().FilledValue.ShouldBe(5010m);
        events.Fills.Single().ExecId.ShouldBe("t-1");
    }

    // An order of nobody in the system that traded: a fill without a client id on its share, for the sessions to reconcile.
    [Test]
    public async Task Trade_of_an_order_of_nobody_is_a_fill_of_nobody_on_its_share()
    {
        var events = new Events();
        await OpenAccountAsync(events);
        StreamOrderState foreign = Message(Guid.NewGuid().ToString()).OrderState;
        foreign.LotSize = 10;
        foreign.Trades.Add(new OrderTrade { TradeId = "t-9", Price = 250m.ToQuotation(), Quantity = 1, DateTime = Timestamp.FromDateTimeOffset(_host.Time.GetUtcNow()) });

        _orders.Send(new OrderStateStreamResponse { OrderState = foreign });

        await EventuallyAsync(() => !events.Fills.IsEmpty, "the fill of nobody");
        ExchangeFill fill = events.Fills.Single();
        (fill.ClientId, fill.Instrument, fill.Quantity, fill.ExchangeOrderId).ShouldBe(("", Sber, 10m, "R-1"));
        _host.Api.Calls.ShouldNotContain(call => call.Method == "GetOrderState");
    }

    // Scenario "Комиссия пришла позже исполнения": the commission shows 10 s after the fill, the read at 15 s finds it.
    [Test]
    public async Task End_of_a_filled_order_waits_for_its_commission()
    {
        DateTimeOffset filled = _host.Time.GetUtcNow();
        _host.Api.On<GetOrderStateRequest, OrderState>("GetOrderState", _ => State(
            OrderExecutionReportStatus.ExecutionReportStatusFill,
            executed: 2,
            commission: _host.Time.GetUtcNow() - filled >= TimeSpan.FromSeconds(10) ? 1.25m : 0m,
            stages: Stage("t-1", 250.5m, 2)));
        var events = new Events();
        await OpenAccountAsync(events);

        _orders.Send(Message(Request.ToString()));

        await AdvanceUntilAsync(() => !events.Orders.IsEmpty, TimeSpan.FromMilliseconds(500), "the end of the order");
        (events.Orders.Single().State, events.Orders.Single().Fee).ShouldBe((ExchangeOrderState.Filled, 1.25m));
        (_host.Time.GetUtcNow() - filled).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(15));
        _host.Api.Calls.Count(call => call.Method == "GetOrderState").ShouldBe(3);
    }

    [Test]
    public async Task End_of_a_filled_order_comes_without_a_commission_after_a_minute()
    {
        DateTimeOffset filled = _host.Time.GetUtcNow();
        _host.Api.On<GetOrderStateRequest, OrderState>("GetOrderState", _ => State(OrderExecutionReportStatus.ExecutionReportStatusFill, executed: 2, stages: Stage("t-1", 250.5m, 2)));
        var events = new Events();
        await OpenAccountAsync(events);

        _orders.Send(Message(Request.ToString()));

        await AdvanceUntilAsync(() => !events.Orders.IsEmpty, TimeSpan.FromSeconds(1), "the end of the order");
        events.Orders.Single().Fee.ShouldBe(0m);
        (_host.Time.GetUtcNow() - filled).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(60));
        _host.Api.Calls.Count(call => call.Method == "GetOrderState").ShouldBe(5);
    }

    // A read of the commission that does not find the order keeps the state read before.
    [Test]
    public async Task Filled_order_that_t_invest_no_longer_finds_comes_with_its_state_read_before()
    {
        var reads = 0;
        _host.Api.On<GetOrderStateRequest, OrderState>("GetOrderState", _ => ++reads == 1
            ? State(OrderExecutionReportStatus.ExecutionReportStatusFill, executed: 2, stages: Stage("t-1", 250.5m, 2))
            : throw new RpcException(new Status(StatusCode.NotFound, "50005")));
        var events = new Events();
        await OpenAccountAsync(events);

        _orders.Send(Message(Request.ToString()));

        await AdvanceUntilAsync(() => !events.Orders.IsEmpty, TimeSpan.FromSeconds(1), "the end of the order");
        (events.Orders.Single().State, events.Orders.Single().FilledQuantity).ShouldBe((ExchangeOrderState.Filled, 20m));
    }

    // An order whose state does not read is not lost without a word: every listener of the account is told restored, so the sessions
    // read their orders again; the message counts as an error of the stream, and the stream goes on.
    [Test]
    public async Task Order_whose_state_does_not_read_tells_every_listener_restored_and_the_stream_goes_on()
    {
        using var requests = new MetricCollector<long>(_host.MeterFactory, TInvestMetrics.MeterName, "profitfox.tinvest.requests");
        _host.Api.Fail("GetOrderState", StatusCode.Unavailable);
        var first = new Events();
        var second = new Events();
        await OpenAccountAsync(first);
        await AccountAsync(second);

        _orders.Send(Message(Request.ToString()));

        await EventuallyAsync(() => first.Restored == 1 && second.Restored == 1, "the restore of every listener");
        first.Orders.ShouldBeEmpty();
        _orders.IsOpen.ShouldBeTrue();
        _orders.Opens.ShouldBe(1);
        requests.GetMeasurementSnapshot()
            .Select(measurement => ((string)measurement.Tags["group"]!, (string)measurement.Tags["method"]!, (string)measurement.Tags["outcome"]!))
            .ShouldContain(("orders", "OrderStateStream", "error"));

        _host.Api.On<GetOrderStateRequest, OrderState>("GetOrderState", _ => State(OrderExecutionReportStatus.ExecutionReportStatusNew));
        _orders.Send(Message(Request.ToString()));

        await EventuallyAsync(() => !first.Orders.IsEmpty && !second.Orders.IsEmpty, "the next order");
        first.Restored.ShouldBe(1);
    }

    // The last listener leaving while an order is read again is no failure of the read.
    [Test]
    public async Task Read_of_an_order_cut_short_by_the_last_listener_leaving_is_no_error()
    {
        using var requests = new MetricCollector<long>(_host.MeterFactory, TInvestMetrics.MeterName, "profitfox.tinvest.requests");
        _host.Api.Fail("GetOrderState", StatusCode.NotFound, "50005");
        IAsyncDisposable subscription = await AccountAsync(new Events());
        await EventuallyAsync(() => _orders.IsOpen, "the stream of orders");
        _orders.Send(Message(Request.ToString()));
        await EventuallyAsync(() => _host.Api.Calls.Any(call => call.Method == "GetOrderState"), "the first read");

        await subscription.DisposeAsync();
        await Task.Delay(50);

        requests.GetMeasurementSnapshot()
            .Select(measurement => ((string)measurement.Tags["group"]!, (string)measurement.Tags["method"]!, (string)measurement.Tags["outcome"]!))
            .ShouldNotContain(("orders", "OrderStateStream", "error"));
    }

    // Scenario "Стрим оборвался": opened again after 1, 2, 5, 10, then 30 s; every listener is told it is back.
    [Test]
    public async Task Broken_stream_of_orders_opens_again_ever_more_slowly_and_tells_it_is_back()
    {
        var first = new Events();
        var second = new Events();
        await OpenAccountAsync(first);
        await AccountAsync(second);

        foreach (int seconds in new[] { 1, 2, 5, 10, 30, 30 })
        {
            int opens = _orders.Opens;
            DateTimeOffset broken = _host.Time.GetUtcNow();
            _orders.Fail(StatusCode.Unavailable);
            await SettleAsync();

            await AdvanceUntilAsync(() => _orders.Opens == opens + 1, TimeSpan.FromMilliseconds(250), "the stream again");
            (_host.Time.GetUtcNow() - broken).ShouldBeInRange(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(250));
        }

        first.Restored.ShouldBe(0);
        _orders.Send(Ping());
        await EventuallyAsync(() => first.Restored == 1 && second.Restored == 1, "the restore of every listener");
    }

    [Test]
    public async Task Listener_that_leaves_while_the_stream_waits_to_open_again_closes_it()
    {
        IAsyncDisposable subscription = await AccountAsync(new Events());
        await EventuallyAsync(() => _orders.IsOpen, "the stream of orders");
        _orders.Fail(StatusCode.Unavailable);
        await Task.Delay(50);

        await subscription.DisposeAsync();
        _host.Time.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(50);

        _orders.Opens.ShouldBe(1);
    }

    // A stream open again after its pause starts over from a pause of a second.
    [Test]
    public async Task Stream_that_was_back_breaks_again_after_a_second()
    {
        await OpenAccountAsync(new Events());
        _orders.Fail(StatusCode.Unavailable);
        await AdvanceUntilAsync(() => _orders.Opens == 2, TimeSpan.FromMilliseconds(250), "the stream again");
        _orders.Send(Ping());
        await Task.Delay(50);
        DateTimeOffset broken = _host.Time.GetUtcNow();

        _orders.Fail(StatusCode.Unavailable);
        await SettleAsync();

        await AdvanceUntilAsync(() => _orders.Opens == 3, TimeSpan.FromMilliseconds(250), "the stream again");
        (_host.Time.GetUtcNow() - broken).ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    // Scenario "Тишина без пингов": a stream with neither messages nor pings for 90 s is lost.
    [Test]
    public async Task Silent_stream_of_orders_is_opened_again()
    {
        var events = new Events();
        await OpenAccountAsync(events);
        DateTimeOffset last = _host.Time.GetUtcNow();

        await AdvanceUntilAsync(() => _orders.Opens == 2, TimeSpan.FromSeconds(1), "the stream again");

        (_host.Time.GetUtcNow() - last).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(90));
        _orders.Streams[0].IsOpen.ShouldBeFalse();
        _orders.Send(Ping());
        await EventuallyAsync(() => events.Restored == 1, "the restore");
    }

    // Task 8.6: opening a stream counts as a request of its group, a broken one as lost.
    [Test]
    public async Task Streams_are_counted_in_the_metrics()
    {
        using var requests = new MetricCollector<long>(_host.MeterFactory, TInvestMetrics.MeterName, "profitfox.tinvest.requests");
        await OpenAccountAsync(new Events());

        _orders.Fail(StatusCode.Unavailable);
        await AdvanceUntilAsync(() => _orders.Opens == 2, TimeSpan.FromMilliseconds(250), "the stream again");
        _orders.Fail(StatusCode.Unavailable);
        await AdvanceUntilAsync(() => _orders.Opens == 3, TimeSpan.FromMilliseconds(250), "the stream again");

        requests.GetMeasurementSnapshot()
            .Select(measurement => ((string)measurement.Tags["group"]!, (string)measurement.Tags["method"]!, (string)measurement.Tags["outcome"]!))
            .ShouldBe([("orders", "OrderStateStream", "ok"), ("orders", "OrderStateStream", "lost"), ("orders", "OrderStateStream", "error")]);
    }

    // Scenario "Два бота на одном аккаунте": the closed candles of two shares come in one stream, each series subscribed once.
    [Test]
    public async Task Series_of_closed_candles_share_one_stream()
    {
        await CandlesAsync(Sber, new Events());
        await CandlesAsync(Sber, new Events());
        await CandlesAsync(Gazp, new Events());

        await EventuallyAsync(() => FirstSubscribed().Count == 2, "two subscriptions");
        _market.Opens.ShouldBe(1);
        _market.Requests[0].PingSettings.PingDelayMs.ShouldBe(30_000);
        SubscribeCandlesRequest[] subscriptions = [.. _market.Requests.Where(request => request.SubscribeCandlesRequest is not null).Select(request => request.SubscribeCandlesRequest)];
        subscriptions.ShouldAllBe(request => request.WaitingClose && request.CandleSourceType == GetCandlesRequest.Types.CandleSource.Exchange && request.SubscriptionAction == SubscriptionAction.Subscribe);
        Subscribed(_market.Streams[0]).ShouldBe([(TInvestFixtures.SberUid, SubscriptionInterval.OneHour), (GazpUid, SubscriptionInterval.OneHour)], ignoreOrder: true);
    }

    [Test]
    public async Task Series_that_comes_to_an_open_stream_is_subscribed_in_it()
    {
        await CandlesAsync(Sber, new Events());
        await EventuallyAsync(() => FirstSubscribed().Count == 1, "the first subscription");

        await CandlesAsync(Gazp, new Events());

        SubscribeCandlesRequest added = _market.Requests.Last().SubscribeCandlesRequest;
        (added.SubscriptionAction, added.Instruments.Single().InstrumentId).ShouldBe((SubscriptionAction.Subscribe, GazpUid));
    }

    // A candle of a series nobody listens to any more, such as one sent before its unsubscription took, reaches nobody.
    [Test]
    public async Task Candle_of_a_series_nobody_listens_to_is_passed_over()
    {
        var sber = new Events();
        await CandlesAsync(Sber, sber);
        await EventuallyAsync(() => FirstSubscribed().Count == 1, "the subscription");
        var open = DateTimeOffset.Parse("2024-03-11T09:00:00Z");

        _market.Send(new MarketDataResponse { Candle = Candle(GazpUid, open, 160m, 3) });
        _market.Send(new MarketDataResponse { Candle = Candle(TInvestFixtures.SberUid, open, 251m, 7) });

        await EventuallyAsync(() => !sber.Candles.IsEmpty, "the candle");
        _market.Opens.ShouldBe(1);
    }

    // The closed candle of the exchange, its volume in shares and its turnover as the history counts them.
    [Test]
    public async Task Closed_candle_reaches_the_listeners_of_its_series()
    {
        var sber = new Events();
        var gazp = new Events();
        await CandlesAsync(Sber, sber);
        await CandlesAsync(Gazp, gazp);
        await EventuallyAsync(() => FirstSubscribed().Count == 2, "two subscriptions");
        var open = DateTimeOffset.Parse("2024-03-11T09:00:00Z");

        _market.Send(new MarketDataResponse { Candle = Candle(TInvestFixtures.SberUid, open, 251m, 7) });

        await EventuallyAsync(() => !sber.Candles.IsEmpty, "the candle");
        Candle candle = sber.Candles.Single();
        (candle.OpenTime, candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value).ShouldBe((open, 250m, 253m, 249m, 251m));
        (candle.Volume.Value, candle.Turnover.Value).ShouldBe((70m, 70m * (253m + 249m + 251m) / 3m));
        gazp.Candles.ShouldBeEmpty();
    }

    // The forming candles come in a stream of their own that does not wait for the close.
    [Test]
    public async Task Forming_candle_comes_from_a_stream_of_its_own()
    {
        var closed = new Events();
        var forming = new Events();
        await CandlesAsync(Sber, closed);
        _subscriptions.Add(await _streams.SubscribeAsync(Sber, Interval.OneHour, forming.Updates.Enqueue, () => forming.Lost++, () => forming.Restored++, () => { }, CancellationToken.None));
        await EventuallyAsync(() => _market.Opens == 2 && _market.Streams.All(stream => Subscribed(stream).Count == 1), "both streams");
        MockTInvestStreamCall<MarketDataRequest, MarketDataResponse> formingStream = _market.Streams.Single(stream => stream.Requests.Any(request => request.SubscribeCandlesRequest is { WaitingClose: false }));
        var open = DateTimeOffset.Parse("2024-03-11T09:00:00Z");
        Tinkoff.InvestApi.V1.Candle update = Candle(TInvestFixtures.SberUid, open, 251m, 7);
        update.LastTradeTs = Timestamp.FromDateTimeOffset(open.AddMinutes(12));

        formingStream.Send(new MarketDataResponse { Candle = update });

        await EventuallyAsync(() => !forming.Updates.IsEmpty, "the forming candle");
        FormingCandle candle = forming.Updates.Single();
        (candle.OpenTime, candle.Close.Value, candle.Volume.Value, candle.IsClosed, candle.ObservedAt).ShouldBe((open, 251m, 70m, false, open.AddMinutes(12)));
        closed.Candles.ShouldBeEmpty();
    }

    [Test]
    public async Task Forming_candle_without_the_time_of_its_last_trade_is_observed_now()
    {
        var forming = new Events();
        _subscriptions.Add(await _streams.SubscribeAsync(Sber, Interval.OneHour, forming.Updates.Enqueue, () => forming.Lost++, () => forming.Restored++, () => { }, CancellationToken.None));
        await EventuallyAsync(() => FirstSubscribed().Count == 1, "the subscription");

        _market.Send(new MarketDataResponse { Candle = Candle(TInvestFixtures.SberUid, DateTimeOffset.Parse("2024-03-11T09:00:00Z"), 251m, 7) });

        await EventuallyAsync(() => !forming.Updates.IsEmpty, "the forming candle");
        forming.Updates.Single().ObservedAt.ShouldBe(_host.Time.GetUtcNow());
    }

    // A series left while its stream is down is not subscribed again when the stream is back; with no series left, none at all.
    [Test]
    public async Task Series_left_while_the_stream_is_down_is_not_subscribed_again()
    {
        IAsyncDisposable sber = await CandlesAsync(Sber, new Events());
        await EventuallyAsync(() => FirstSubscribed().Count == 1, "the subscription");
        _market.Fail(StatusCode.Unavailable);
        await Task.Delay(50);

        await sber.DisposeAsync();
        await AdvanceUntilAsync(() => _market.Opens == 2, TimeSpan.FromMilliseconds(250), "the stream again");
        await Task.Delay(50);

        _market.Streams[1].Requests.ShouldAllBe(request => request.SubscribeCandlesRequest == null);
    }

    // The token of the candles is the one of a T-Invest account of storage: without one the stream waits and opens again later.
    [Test]
    public async Task Stream_of_candles_without_a_tinvest_account_opens_again_once_there_is_one()
    {
        await CandlesAsync(Sber, new Events());
        await EventuallyAsync(() => FirstSubscribed().Count == 1, "the subscription");
        _market.Send(Ping(market: true));
        IReadOnlyList<ProfitFox.Domain.Entities.ExchangeAccounts.ExchangeAccount> accounts = await _host.Accounts.GetAllAsync(CancellationToken.None);
        _host.Accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _host.Time.Advance(TInvestTokenSource.Lifetime);
        _market.Fail(StatusCode.Unavailable);
        await AdvanceUntilAsync(() => _host.Accounts.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "GetAllAsync") >= 3, TimeSpan.FromMilliseconds(500), "the token asked for again");
        _market.Opens.ShouldBe(1);

        _host.Accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns(accounts);

        await AdvanceUntilAsync(() => _market.Opens == 2, TimeSpan.FromSeconds(1), "the stream again");
    }

    // Scenario "Последний слушатель ушёл": the series is taken off, the others stay.
    [Test]
    public async Task Last_listener_of_a_series_takes_it_off()
    {
        IAsyncDisposable sber = await CandlesAsync(Sber, new Events());
        await CandlesAsync(Gazp, new Events());
        await EventuallyAsync(() => FirstSubscribed().Count == 2, "two subscriptions");

        await sber.DisposeAsync();

        SubscribeCandlesRequest off = _market.Requests.Last().SubscribeCandlesRequest;
        (off.SubscriptionAction, off.Instruments.Single().InstrumentId).ShouldBe((SubscriptionAction.Unsubscribe, TInvestFixtures.SberUid));
        _market.IsOpen.ShouldBeTrue();
    }

    // A broken stream of candles subscribes every series again, and its listeners are told it is lost and then back.
    [Test]
    public async Task Broken_stream_of_candles_subscribes_its_series_again()
    {
        var sber = new Events();
        await CandlesAsync(Sber, sber);
        await CandlesAsync(Gazp, new Events());
        await EventuallyAsync(() => FirstSubscribed().Count == 2, "two subscriptions");
        _market.Send(Ping(market: true));
        await Task.Delay(50);

        _market.Fail(StatusCode.Unavailable);

        await AdvanceUntilAsync(() => _market.Opens == 2 && Subscribed(_market.Streams[1]).Count == 2, TimeSpan.FromMilliseconds(250), "the stream again");
        _market.Send(Ping(market: true));
        await EventuallyAsync(() => sber.Restored == 1, "the restore");
    }

    [Test]
    public async Task Broken_stream_of_forming_candles_tells_its_listeners_it_is_lost()
    {
        var forming = new Events();
        _subscriptions.Add(await _streams.SubscribeAsync(Sber, Interval.OneHour, forming.Updates.Enqueue, () => forming.Lost++, () => forming.Restored++, () => { }, CancellationToken.None));
        await EventuallyAsync(() => _market.IsOpen, "the stream");
        _market.Send(Ping(market: true));
        await Task.Delay(50);

        _market.Fail(StatusCode.Unavailable);

        await EventuallyAsync(() => forming.Lost == 1, "the loss");
        await AdvanceUntilAsync(() => _market.Opens == 2, TimeSpan.FromMilliseconds(250), "the stream again");
        _market.Send(Ping(market: true));
        await EventuallyAsync(() => forming.Restored == 1, "the restore");
    }

    // Decision 9: a minute after the end of the trading day by the schedule, the listeners of the closed candles read again what they keep.
    [Test]
    public async Task Listeners_of_closed_candles_are_told_restored_a_minute_after_the_end_of_the_trading_day()
    {
        _host.Api.On<TradingSchedulesRequest, TradingSchedulesResponse>("TradingSchedules", _ => new TradingSchedulesResponse
        {
            Exchanges =
            {
                new TradingSchedule
                {
                    Exchange = TInvestFixtures.SberExchange,
                    Days =
                    {
                        new TradingDay
                        {
                            Date = Timestamp.FromDateTimeOffset(DateTimeOffset.Parse("2024-03-11T00:00:00Z")),
                            Intervals = { Session("regular_trading_session_main", "2024-03-11T07:00:00Z", "2024-03-11T10:30:00Z") }
                        },
                        new TradingDay
                        {
                            Date = Timestamp.FromDateTimeOffset(DateTimeOffset.Parse("2024-03-12T00:00:00Z")),
                            Intervals = { Session("regular_trading_session_main", "2024-03-12T07:00:00Z", "2024-03-12T15:40:00Z") }
                        }
                    }
                }
            }
        });
        var events = new Events();
        var forming = new Events();
        await CandlesAsync(Sber, events);
        _subscriptions.Add(await _streams.SubscribeAsync(Sber, Interval.OneHour, forming.Updates.Enqueue, () => forming.Lost++, () => forming.Restored++, () => { }, CancellationToken.None));

        await AdvanceUntilAsync(() => events.Restored == 1, TimeSpan.FromSeconds(10), "the restore at the end of the day");

        _host.Time.GetUtcNow().ShouldBeInRange(DateTimeOffset.Parse("2024-03-11T10:31:00Z"), DateTimeOffset.Parse("2024-03-11T10:31:10Z"));
        forming.Restored.ShouldBe(0);
    }

    // A schedule that does not read is asked again an hour later.
    [Test]
    public async Task Schedule_that_does_not_read_is_asked_again_an_hour_later()
    {
        await CandlesAsync(Sber, new Events());
        await EventuallyAsync(() => _host.Api.Calls.Count(call => call.Method == "TradingSchedules") == 1, "the schedule");
        DateTimeOffset asked = _host.Time.GetUtcNow();

        await AdvanceUntilAsync(() => _host.Api.Calls.Count(call => call.Method == "TradingSchedules") == 2, TimeSpan.FromMinutes(1), "the schedule again");

        (_host.Time.GetUtcNow() - asked).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromHours(1));
    }

    private async Task OpenAccountAsync(Events events)
    {
        await AccountAsync(events);
        await EventuallyAsync(() => _orders.IsOpen, "the stream of orders");
        _orders.Send(Ping());
        await Task.Delay(50);
    }

    private async Task<IAsyncDisposable> AccountAsync(Events events)
    {
        IAsyncDisposable subscription = await _streams.SubscribeAccountAsync(Key, events.Orders.Enqueue, events.Fills.Enqueue, () => events.Restored++, CancellationToken.None);
        _subscriptions.Add(subscription);

        return subscription;
    }

    private async Task<IAsyncDisposable> CandlesAsync(Instrument instrument, Events events)
    {
        IAsyncDisposable subscription = await _streams.SubscribeCandlesAsync(instrument, Interval.OneHour, events.Candles.Enqueue, () => events.Restored++, CancellationToken.None);
        _subscriptions.Add(subscription);

        return subscription;
    }

    private List<(string Uid, SubscriptionInterval Interval)> FirstSubscribed()
    {
        return _market.Opens > 0 ? Subscribed(_market.Streams[0]) : [];
    }

    private static List<(string Uid, SubscriptionInterval Interval)> Subscribed(MockTInvestStreamCall<MarketDataRequest, MarketDataResponse> stream)
    {
        var subscribed = new List<(string, SubscriptionInterval)>();

        foreach (SubscribeCandlesRequest request in stream.Requests.Where(request => request.SubscribeCandlesRequest is not null).Select(request => request.SubscribeCandlesRequest))
        {
            foreach (CandleInstrument instrument in request.Instruments)
            {
                if (request.SubscriptionAction == SubscriptionAction.Subscribe)
                {
                    subscribed.Add((instrument.InstrumentId, instrument.Interval));
                }
                else
                {
                    subscribed.Remove((instrument.InstrumentId, instrument.Interval));
                }
            }
        }

        return subscribed;
    }

    /// <summary>Real time for a stream to take a break in and start its pause on the clock, before the clock moves.</summary>
    private static Task SettleAsync()
    {
        return Task.Delay(200);
    }

    /// <summary>Real time passes for the work the streams have started; the clock stands.</summary>
    private static async Task EventuallyAsync(Func<bool> condition, string what)
    {
        for (var attempt = 0; attempt < 300 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        condition().ShouldBeTrue($"Waited in vain for {what}.");
    }

    /// <summary>The clock moves a step at a time until the condition holds, so the pauses of the streams end.</summary>
    private async Task AdvanceUntilAsync(Func<bool> condition, TimeSpan step, string what)
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            await Task.Delay(10);

            if (condition())
            {
                return;
            }

            _host.Time.Advance(step);
        }

        condition().ShouldBeTrue($"Waited in vain for {what}.");
    }

    private static OrderStateStreamResponse Message(string request)
    {
        return new OrderStateStreamResponse { OrderState = new StreamOrderState { OrderId = "R-1", OrderRequestId = request, Ticker = "SBER", AccountId = AccountNumber } };
    }

    private static OrderStateStreamResponse Ping()
    {
        return new OrderStateStreamResponse { Ping = new Tinkoff.InvestApi.V1.Ping() };
    }

    private static MarketDataResponse Ping(bool market)
    {
        return new MarketDataResponse { Ping = new Tinkoff.InvestApi.V1.Ping() };
    }

    private static OrderState State(OrderExecutionReportStatus status, long executed = 0, decimal commission = 0m, params OrderStage[] stages)
    {
        return new OrderState
        {
            OrderId = "R-1",
            OrderRequestId = Request.ToString(),
            ExecutionReportStatus = status,
            InstrumentUid = TInvestFixtures.SberUid,
            LotsExecuted = executed,
            ExecutedCommission = Money(commission),
            Stages = { stages }
        };
    }

    private static OrderStage Stage(string id, decimal price, long lots)
    {
        return new OrderStage { TradeId = id, Price = Money(price), Quantity = lots, ExecutionTime = Timestamp.FromDateTimeOffset(DateTimeOffset.Parse("2024-03-11T09:15:00Z")) };
    }

    private static MoneyValue Money(decimal value)
    {
        var quotation = value.ToQuotation();

        return new MoneyValue { Currency = "rub", Units = quotation.Units, Nano = quotation.Nano };
    }

    private static Tinkoff.InvestApi.V1.Candle Candle(string uid, DateTimeOffset open, decimal close, long lots)
    {
        return new Tinkoff.InvestApi.V1.Candle
        {
            InstrumentUid = uid,
            Interval = SubscriptionInterval.OneHour,
            Time = Timestamp.FromDateTimeOffset(open),
            Open = (close - 1m).ToQuotation(),
            High = (close + 2m).ToQuotation(),
            Low = (close - 2m).ToQuotation(),
            Close = close.ToQuotation(),
            Volume = lots
        };
    }

    private static TradingInterval Session(string type, string start, string end)
    {
        return new TradingInterval
        {
            Type = type,
            Interval = new TradingInterval.Types.TimeInterval
            {
                StartTs = Timestamp.FromDateTimeOffset(DateTimeOffset.Parse(start)),
                EndTs = Timestamp.FromDateTimeOffset(DateTimeOffset.Parse(end))
            }
        };
    }

    private static Share Gazp10()
    {
        Share share = TInvestFixtures.Sber();
        share.Uid = GazpUid;
        share.Ticker = "GAZP";

        return share;
    }

    /// <summary>What a listener got.</summary>
    private sealed class Events
    {
        public ConcurrentQueue<ExchangeOrder> Orders { get; } = new ConcurrentQueue<ExchangeOrder>();

        public ConcurrentQueue<ExchangeFill> Fills { get; } = new ConcurrentQueue<ExchangeFill>();

        public ConcurrentQueue<Candle> Candles { get; } = new ConcurrentQueue<Candle>();

        public ConcurrentQueue<FormingCandle> Updates { get; } = new ConcurrentQueue<FormingCandle>();

        public int Restored { get; set; }

        public int Lost { get; set; }
    }
}