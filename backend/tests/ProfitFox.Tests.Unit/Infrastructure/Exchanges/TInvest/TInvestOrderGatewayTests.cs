using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using NSubstitute;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

using Instrument = ProfitFox.Domain.ValueObjects.MarketData.Instrument;
using OrderSide = ProfitFox.Domain.Entities.Strategies.Enums.OrderSide;
using OrderType = ProfitFox.Domain.Entities.Strategies.Enums.OrderType;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Decisions 4, 5 and 6: an order of a bot on T-Invest in lots, a limit one for the day, sent only while the session takes it.</summary>
public class TInvestOrderGatewayTests
{
    private const string AccountNumber = "2000000001";
    private static readonly ExchangeKey Key = new ExchangeKey(AccountNumber, TInvestTestHost.Token, Exchange.TInvest);
    private static readonly Instrument Sber = new Instrument(MarketCategory.Moex, "SBER");
    private static readonly Guid Request = Guid.Parse("6f1c2c1e-6d0e-4b8b-9f43-2f6a0c1b2d3e");

    private TInvestTestHost _host = null!;
    private ITInvestOrderIds _ids = null!;
    private TInvestOrderGateway _gateway = null!;
    private GetTradingStatusResponse _status = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new TInvestTestHost();
        _ids = Substitute.For<ITInvestOrderIds>();
        _ids.EnsureAsync("b-1", AccountNumber, TInvestFixtures.SberUid, Arg.Any<CancellationToken>()).Returns(Request);
        _ids.RequestOfAsync("b-1", Arg.Any<CancellationToken>()).Returns(Request);
        _status = new GetTradingStatusResponse { ApiTradeAvailableFlag = true, LimitOrderAvailableFlag = true, MarketOrderAvailableFlag = true };
        _host.Api
            .On<InstrumentRequest, ShareResponse>("ShareBy", _ => new ShareResponse { Instrument = TInvestFixtures.Sber() })
            .On<GetTradingStatusRequest, GetTradingStatusResponse>("GetTradingStatus", _ => _status)
            .On<GetOrderBookRequest, GetOrderBookResponse>("GetOrderBook", _ => new GetOrderBookResponse { LimitDown = 240m.ToQuotation(), LimitUp = 260m.ToQuotation() })
            .On<PostOrderRequest, PostOrderResponse>("PostOrder", _ => new PostOrderResponse { OrderId = "R-1" })
            .On<CancelOrderRequest, CancelOrderResponse>("CancelOrder", _ => new CancelOrderResponse());
        _gateway = new TInvestOrderGateway(_host.Call, _host.Shares, _ids, new TInvestTradingStatuses(_host.Call, _host.Time), _host.Time);
    }

    [TearDown]
    public void TearDown()
    {
        _host.Dispose();
    }

    // Scenario "Лимитная покупка": 20 shares at 250.5 with a lot of 10 go as a limit of the day for 2 lots from the account.
    [Test]
    public async Task Limit_buy_goes_for_the_day_in_lots_with_the_request_id_written_before()
    {
        OrderOutcome outcome = await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, 250.5m, 20m, null));

        outcome.ShouldBe(new OrderOutcome(OrderOutcomeKind.Accepted, "R-1"));
        PostOrderRequest sent = Posted().Single();
        (sent.AccountId, sent.InstrumentId, sent.Quantity, sent.Price.ToDecimal(), sent.Direction).ShouldBe((AccountNumber, TInvestFixtures.SberUid, 2L, 250.5m, OrderDirection.Buy));
        (sent.OrderType, sent.TimeInForce, sent.OrderId).ShouldBe((Tinkoff.InvestApi.V1.OrderType.Limit, TimeInForceType.TimeInForceDay, Request.ToString()));
        Received.InOrder(() =>
        {
            _ids.EnsureAsync("b-1", AccountNumber, TInvestFixtures.SberUid, Arg.Any<CancellationToken>());
            _ids.SetExchangeIdAsync("b-1", "R-1", Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task Market_sell_goes_without_a_price_and_a_time_in_force()
    {
        await PlaceAsync(new OrderRequest("b-1", OrderSide.Sell, OrderType.Market, null, 10m, null));

        PostOrderRequest sent = Posted().Single();
        (sent.OrderType, sent.Direction, sent.Quantity, sent.Price, sent.TimeInForce).ShouldBe((Tinkoff.InvestApi.V1.OrderType.Market, OrderDirection.Sell, 1L, null, TimeInForceType.TimeInForceUnspecified));
    }

    // Scenario "Ответ на отправку потерян": a send again goes with the same request id.
    [Test]
    public async Task Order_sent_again_goes_with_the_same_request_id()
    {
        await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, 250.5m, 20m, null));
        await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, 250.5m, 20m, null));

        Posted().Select(sent => sent.OrderId).ShouldBe([Request.ToString(), Request.ToString()]);
    }

    // Scenario "Не целое число лотов".
    [Test]
    public async Task Order_not_of_whole_lots_is_refused_before_it_goes()
    {
        OrderOutcome outcome = await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, 250.5m, 15m, null));

        outcome.ShouldBe(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "The order is not a whole number of lots: 15 shares at a lot of 10."));
        Posted().ShouldBeEmpty();
        await _ids.DidNotReceiveWithAnyArgs().EnsureAsync(default!, default!, default!, default);
    }

    // A stop and a target, a close that only reduces, an order by amount: none of them goes to T-Invest in this change.
    [TestCase("protection")]
    [TestCase("reduce")]
    [TestCase("amount")]
    public async Task Order_t_invest_does_not_take_in_this_change_is_refused_before_it_goes(string kind)
    {
        OrderRequest order = kind switch
        {
            "protection" => new OrderRequest("b-1", OrderSide.Buy, OrderType.Market, null, 10m, null, new Protection(260m, 240m)),
            "reduce" => new OrderRequest("b-1", OrderSide.Sell, OrderType.Market, null, 10m, null, ReduceOnly: true),
            _ => new OrderRequest("b-1", OrderSide.Buy, OrderType.Market, null, null, 1000m)
        };

        OrderOutcome outcome = await PlaceAsync(order);

        outcome.ShouldBe(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "T-Invest takes only orders by quantity without a stop, a target or reduce-only."));
        Posted().ShouldBeEmpty();
    }

    // Decision 5: the request was known already; the order is the one the exchange has under the request id.
    [Test]
    public async Task Request_known_already_is_the_order_taken()
    {
        _host.Api.Fail("PostOrder", StatusCode.InvalidArgument, "30057");
        _host.Api.On<GetOrderStateRequest, OrderState>("GetOrderState", _ => new OrderState { OrderId = "R-9" });

        OrderOutcome outcome = await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, 250.5m, 20m, null));

        outcome.ShouldBe(new OrderOutcome(OrderOutcomeKind.Accepted, "R-9"));
        var read = (GetOrderStateRequest)_host.Api.Calls.Single(call => call.Method == "GetOrderState").Request;
        (read.AccountId, read.OrderId, read.OrderIdType).ShouldBe((AccountNumber, Request.ToString(), OrderIdType.Request));
        await _ids.Received(1).SetExchangeIdAsync("b-1", "R-9", Arg.Any<CancellationToken>());
    }

    // T-Invest tells the request before its state knows it (ADR 0036): the send is tried again with the same request id.
    [Test]
    public async Task Request_known_already_whose_state_is_not_known_yet_is_tried_again()
    {
        _host.Api.Fail("PostOrder", StatusCode.InvalidArgument, "30057");
        _host.Api.Fail("GetOrderState", StatusCode.NotFound, "50005");

        OrderOutcome outcome = await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, 250.5m, 20m, null));

        (outcome.Kind, outcome.RetryAfter).ShouldBe((OrderOutcomeKind.Transient, null));
    }

    // Scenarios "Отказ биржи «сессия закрыта»" and "Заявка ночью": the next attempt waits for the opening by the schedule.
    [Test]
    public async Task Refusal_of_a_closed_session_waits_for_the_next_session()
    {
        _host.Time.SetUtcNow(DateTimeOffset.Parse("2024-03-10T21:30:00Z"));
        _host.Api.Fail("PostOrder", StatusCode.FailedPrecondition, "30098");
        OpensAt("2024-03-11T03:50:00Z");

        OrderOutcome outcome = await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, 250.5m, 20m, null));

        (outcome.Kind, outcome.RetryAfter).ShouldBe((OrderOutcomeKind.Transient, TimeSpan.FromMinutes(380)));
    }

    // Scenario "Сессия закрыта по статусу": the order does not go.
    [Test]
    public async Task Order_the_status_does_not_take_waits_for_the_next_session_without_going()
    {
        _host.Time.SetUtcNow(DateTimeOffset.Parse("2024-03-10T21:30:00Z"));
        _status = new GetTradingStatusResponse { ApiTradeAvailableFlag = true, LimitOrderAvailableFlag = false, MarketOrderAvailableFlag = true };
        OpensAt("2024-03-11T03:50:00Z");

        OrderOutcome outcome = await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, 250.5m, 20m, null));

        (outcome.Kind, outcome.RetryAfter).ShouldBe((OrderOutcomeKind.Transient, TimeSpan.FromMinutes(380)));
        Posted().ShouldBeEmpty();
    }

    // A bot that wakes at the opening while the status still takes no orders waits a minute, not until the session after.
    [Test]
    public async Task Order_the_status_does_not_take_in_the_first_minute_of_the_session_waits_a_minute()
    {
        _host.Time.SetUtcNow(DateTimeOffset.Parse("2024-03-11T03:50:30Z"));
        _status = new GetTradingStatusResponse { ApiTradeAvailableFlag = false };
        OpensAt("2024-03-11T03:50:00Z");

        OrderOutcome outcome = await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, 250.5m, 20m, null));

        (outcome.Kind, outcome.RetryAfter).ShouldBe((OrderOutcomeKind.Transient, TimeSpan.FromMinutes(1)));
    }

    // Scenario "Расписание не читается".
    [Test]
    public async Task Closed_session_without_a_schedule_waits_a_minute()
    {
        _status = new GetTradingStatusResponse { ApiTradeAvailableFlag = false };
        _host.Api.Fail("TradingSchedules", StatusCode.Unavailable);

        OrderOutcome outcome = await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, 250.5m, 20m, null));

        (outcome.Kind, outcome.RetryAfter).ShouldBe((OrderOutcomeKind.Transient, TimeSpan.FromMinutes(1)));
    }

    // Scenario "Цена вне коридора": by the book the order does not go; by the refusal 30099 it waits the same.
    [TestCase(239.5, false)]
    [TestCase(250.5, true)]
    public async Task Price_out_of_the_corridor_waits_a_minute(decimal price, bool refused)
    {
        _host.Api.Fail("PostOrder", StatusCode.FailedPrecondition, "30099");

        OrderOutcome outcome = await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, price, 20m, null));

        (outcome.Kind, outcome.RetryAfter).ShouldBe((OrderOutcomeKind.Transient, TimeSpan.FromMinutes(1)));
        Posted().Count.ShouldBe(refused ? 1 : 0);
    }

    // The code of the refusal reaches the reason, never the text of the broker.
    [TestCase(StatusCode.InvalidArgument, "30034")]
    [TestCase(StatusCode.InvalidArgument, "30015")]
    [TestCase(StatusCode.InvalidArgument, "30016")]
    [TestCase(StatusCode.InvalidArgument, "30080")]
    [TestCase(StatusCode.InvalidArgument, "30017")]
    [TestCase(StatusCode.InvalidArgument, "30018")]
    [TestCase(StatusCode.InvalidArgument, "30100")]
    [TestCase(StatusCode.InvalidArgument, "30078")]
    [TestCase(StatusCode.FailedPrecondition, "30052")]
    [TestCase(StatusCode.FailedPrecondition, "30079")]
    [TestCase(StatusCode.FailedPrecondition, "30042")]
    [TestCase(StatusCode.FailedPrecondition, "30051")]
    [TestCase(StatusCode.OutOfRange, "30999")]
    public async Task Refusal_another_attempt_meets_again_is_permanent_with_its_code(StatusCode status, string code)
    {
        _host.Api.Fail("PostOrder", status, code, new Metadata { { "message", "Недостаточно средств" } });

        OrderOutcome outcome = await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, 250.5m, 20m, null));

        outcome.ShouldBe(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: $"T-Invest refused the order: code {code}."));
    }

    [Test]
    public async Task Refusal_without_a_code_is_permanent()
    {
        _host.Api.Fail("PostOrder", StatusCode.FailedPrecondition, "some broker text");

        OrderOutcome outcome = await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, 250.5m, 20m, null));

        outcome.ShouldBe(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "T-Invest refused the request (FailedPrecondition)."));
    }

    [TestCase(StatusCode.Unauthenticated)]
    [TestCase(StatusCode.PermissionDenied)]
    public async Task Token_t_invest_does_not_accept_is_permanent(StatusCode status)
    {
        _host.Api.Fail("PostOrder", status, "40003");

        OrderOutcome outcome = await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, 250.5m, 20m, null));

        outcome.ShouldBe(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "T-Invest does not accept the token."));
    }

    // An unknown outcome of a send is safe to repeat: the request id is the same.
    [TestCase(StatusCode.Unavailable)]
    [TestCase(StatusCode.Internal)]
    [TestCase(StatusCode.Aborted)]
    [TestCase(StatusCode.DeadlineExceeded)]
    [TestCase(StatusCode.ResourceExhausted)]
    public async Task Failure_of_t_invest_is_transient(StatusCode status)
    {
        _host.Api.Fail("PostOrder", status);

        OrderOutcome outcome = await PlaceAsync(new OrderRequest("b-1", OrderSide.Buy, OrderType.Limit, 250.5m, 20m, null));

        (outcome.Kind, outcome.RetryAfter).ShouldBe((OrderOutcomeKind.Transient, null));
    }

    [Test]
    public async Task Cancel_goes_by_the_request_id()
    {
        OrderOutcome outcome = await _gateway.CancelAsync(Key, Sber, "b-1", CancellationToken.None);

        outcome.Kind.ShouldBe(OrderOutcomeKind.Accepted);
        var sent = (CancelOrderRequest)_host.Api.Calls.Single(call => call.Method == "CancelOrder").Request;
        (sent.AccountId, sent.OrderId, sent.OrderIdType).ShouldBe((AccountNumber, Request.ToString(), OrderIdType.Request));
    }

    [Test]
    public async Task Cancel_by_the_exchange_id_goes_by_it()
    {
        await _gateway.CancelByExchangeIdAsync(Key, Sber, "R-1", CancellationToken.None);

        var sent = (CancelOrderRequest)_host.Api.Calls.Single(call => call.Method == "CancelOrder").Request;
        (sent.AccountId, sent.OrderId, sent.OrderIdType).ShouldBe((AccountNumber, "R-1", OrderIdType.Exchange));
    }

    // An order that never went to T-Invest is not there to cancel.
    [Test]
    public async Task Cancel_of_an_order_without_a_request_is_done_already_without_a_call()
    {
        (await _gateway.CancelAsync(Key, Sber, "b-2", CancellationToken.None)).Kind.ShouldBe(OrderOutcomeKind.AlreadyDone);

        _host.Api.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Cancel_of_an_order_t_invest_does_not_know_is_done_already()
    {
        _host.Api.Fail("CancelOrder", StatusCode.NotFound, "50005");

        (await _gateway.CancelAsync(Key, Sber, "b-1", CancellationToken.None)).Kind.ShouldBe(OrderOutcomeKind.AlreadyDone);
    }

    // A refused cancel of an order that ended meanwhile is done already; of an order that still works, a refusal.
    [TestCase(OrderExecutionReportStatus.ExecutionReportStatusFill, OrderOutcomeKind.AlreadyDone)]
    [TestCase(OrderExecutionReportStatus.ExecutionReportStatusCancelled, OrderOutcomeKind.AlreadyDone)]
    [TestCase(OrderExecutionReportStatus.ExecutionReportStatusRejected, OrderOutcomeKind.AlreadyDone)]
    [TestCase(OrderExecutionReportStatus.ExecutionReportStatusNew, OrderOutcomeKind.Permanent)]
    public async Task Refused_cancel_is_done_already_only_for_an_order_that_ended(OrderExecutionReportStatus status, OrderOutcomeKind kind)
    {
        _host.Api.Fail("CancelOrder", StatusCode.FailedPrecondition, "30059");
        _host.Api.On<GetOrderStateRequest, OrderState>("GetOrderState", _ => new OrderState { OrderId = "R-1", ExecutionReportStatus = status });

        OrderOutcome outcome = await _gateway.CancelAsync(Key, Sber, "b-1", CancellationToken.None);

        outcome.Kind.ShouldBe(kind);
        var read = (GetOrderStateRequest)_host.Api.Calls.Single(call => call.Method == "GetOrderState").Request;
        (read.OrderId, read.OrderIdType).ShouldBe((Request.ToString(), OrderIdType.Request));

        if (kind == OrderOutcomeKind.Permanent)
        {
            outcome.Reason.ShouldBe("T-Invest refused the cancel: code 30059.");
        }
    }

    [Test]
    public async Task Refused_cancel_of_an_order_t_invest_does_not_know_is_done_already()
    {
        _host.Api.Fail("CancelOrder", StatusCode.FailedPrecondition, "30059");
        _host.Api.Fail("GetOrderState", StatusCode.NotFound, "50005");

        (await _gateway.CancelAsync(Key, Sber, "b-1", CancellationToken.None)).Kind.ShouldBe(OrderOutcomeKind.AlreadyDone);
    }

    [Test]
    public async Task Failure_of_t_invest_on_a_cancel_is_transient()
    {
        _host.Api.Fail("CancelOrder", StatusCode.Unavailable);

        (await _gateway.CancelAsync(Key, Sber, "b-1", CancellationToken.None)).Kind.ShouldBe(OrderOutcomeKind.Transient);
    }

    private Task<OrderOutcome> PlaceAsync(OrderRequest order)
    {
        return _gateway.PlaceAsync(Key, Sber, order, CancellationToken.None);
    }

    private List<PostOrderRequest> Posted()
    {
        return [.. _host.Api.Calls.Where(call => call.Method == "PostOrder").Select(call => (PostOrderRequest)call.Request)];
    }

    private void OpensAt(string start)
    {
        var opening = DateTimeOffset.Parse(start);
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
                            Date = Timestamp.FromDateTimeOffset(opening.UtcDateTime.Date),
                            IsTradingDay = true,
                            Intervals =
                            {
                                new TradingInterval
                                {
                                    Type = "opening_auction_morning",
                                    Interval = new TradingInterval.Types.TimeInterval { StartTs = Timestamp.FromDateTimeOffset(opening), EndTs = Timestamp.FromDateTimeOffset(opening.AddMinutes(10)) }
                                }
                            }
                        }
                    }
                }
            }
        });
    }
}