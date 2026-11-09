using Grpc.Core;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

using ExchangeOrderState = ProfitFox.Application.Ports.LiveTrading.ExchangeOrderState;
using Instrument = ProfitFox.Domain.ValueObjects.MarketData.Instrument;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Decision 8: what an account holds on T-Invest, read with the token of its key; nothing here sends an order.</summary>
public class TInvestLedgerTests
{
    private const string AccountNumber = "2000000001";
    private static readonly ExchangeKey Key = new ExchangeKey(AccountNumber, TInvestTestHost.Token, Exchange.TInvest);
    private static readonly Instrument Sber = new Instrument(MarketCategory.Moex, "SBER");
    private static readonly Guid Request = Guid.Parse("6f1c2c1e-6d0e-4b8b-9f43-2f6a0c1b2d3e");

    private TInvestTestHost _host = null!;
    private ITInvestOrderIds _ids = null!;
    private TInvestLedger _ledger = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new TInvestTestHost();
        _ids = Substitute.For<ITInvestOrderIds>();
        _ids.RequestOfAsync("b-1", Arg.Any<CancellationToken>()).Returns(Request);
        _ids.ClientIdOfAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        _ids.ClientIdOfAsync(Request, Arg.Any<CancellationToken>()).Returns("b-1");
        _host.Api.On<InstrumentRequest, ShareResponse>("ShareBy", _ => new ShareResponse { Instrument = TInvestFixtures.Sber() });
        _ledger = new TInvestLedger(_host.Call, _host.Shares, _ids);
    }

    [TearDown]
    public void TearDown()
    {
        _host.Dispose();
    }

    // add-idea-trade-leverage: MOEX has no symbol of linear to read; the refusal goes nowhere near T-Invest.
    [Test]
    public async Task Linear_position_is_refused_without_a_call()
    {
        ExchangeException error = await Should.ThrowAsync<ExchangeException>(() => _ledger.GetLinearPositionAsync(Key, Sber, CancellationToken.None));

        error.Message.ShouldBe("T-Invest has no leverage of a symbol.");
        _host.Api.Calls.ShouldBeEmpty();
    }

    // Decision 5 of recognize-liquidations-in-idea-trades: the shares of MOEX are never liquidated in this trading.
    [Test]
    public async Task Forced_fills_are_refused_without_a_call()
    {
        ExchangeException error = await Should.ThrowAsync<ExchangeException>(() => _ledger.GetForcedFillsAsync(Key, Sber, DateTimeOffset.UnixEpoch, CancellationToken.None));

        error.Message.ShouldBe("T-Invest has no liquidations.");
        _host.Api.Calls.ShouldBeEmpty();
    }

    // Scenario "Рубли на счёте T-Invest": 50 000 ₽ of which 10 000 are blocked by an order.
    [Test]
    public async Task Free_roubles_are_the_money_less_what_orders_block()
    {
        _host.Api.On<PositionsRequest, PositionsResponse>("GetPositions", _ => new PositionsResponse
        {
            Money = { Money(50_000m, "rub"), Money(100m, "usd") },
            Blocked = { Money(10_000m, "rub") }
        });

        (await _ledger.GetFreeAsync(Key, "RUB", CancellationToken.None)).ShouldBe(40_000m);

        ((PositionsRequest)_host.Api.Calls.Single(call => call.Method == "GetPositions").Request).AccountId.ShouldBe(AccountNumber);
        _host.Api.Calls.Single(call => call.Method == "GetPositions").Headers.GetValue("authorization").ShouldBe($"Bearer {TInvestTestHost.Token}");
    }

    [Test]
    public async Task Account_without_the_coin_has_none_free()
    {
        _host.Api.On<PositionsRequest, PositionsResponse>("GetPositions", _ => new PositionsResponse { Money = { Money(100m, "usd") } });

        (await _ledger.GetFreeAsync(Key, "RUB", CancellationToken.None)).ShouldBe(0m);
    }

    [Test]
    public async Task Account_on_t_invest_keeps_no_hedge()
    {
        (await _ledger.IsHedgeModeAsync(Key, Sber, CancellationToken.None)).ShouldBeFalse();

        _host.Api.Calls.ShouldBeEmpty();
    }

    // Scenario "Позиция совпадает": 20 shares of SBER, 10 of them blocked by a sell.
    [Test]
    public async Task Holding_of_a_share_counts_the_shares_blocked_by_orders()
    {
        _host.Api.On<PositionsRequest, PositionsResponse>("GetPositions", _ => new PositionsResponse
        {
            Securities =
            {
                new PositionsSecurities { InstrumentUid = TInvestFixtures.SberUid, Balance = 10, Blocked = 10 },
                new PositionsSecurities { InstrumentUid = "other", Balance = 500 }
            }
        });

        (await _ledger.GetHoldingAsync(Key, Sber, "SBER", CancellationToken.None)).ShouldBe(20m);
    }

    // The active orders of the account on the share, told to the bots by the request id; an order of nobody has no client id.
    [Test]
    public async Task Open_orders_of_the_share_are_told_by_their_request_id()
    {
        _host.Api.On<GetOrdersRequest, GetOrdersResponse>("GetOrders", _ => new GetOrdersResponse
        {
            Orders =
            {
                State("R-1", Request.ToString(), OrderExecutionReportStatus.ExecutionReportStatusNew),
                State("R-2", Guid.NewGuid().ToString(), OrderExecutionReportStatus.ExecutionReportStatusPartiallyfill, executed: 1),
                State("R-3", "not-a-uuid", OrderExecutionReportStatus.ExecutionReportStatusNew),
                State("R-4", Request.ToString(), OrderExecutionReportStatus.ExecutionReportStatusNew, uid: "other")
            }
        });

        IReadOnlyList<ExchangeOrder> orders = await _ledger.GetOpenOrdersAsync(Key, Sber, CancellationToken.None);

        orders.Select(order => (order.ClientId, order.ExchangeId, order.FilledQuantity)).ShouldBe([("b-1", "R-1", 0m), ("", "R-2", 10m), ("", "R-3", 0m)]);
        ((GetOrdersRequest)_host.Api.Calls.Single(call => call.Method == "GetOrders").Request).AccountId.ShouldBe(AccountNumber);
    }

    [Test]
    public async Task Order_of_a_bot_is_read_by_its_request_id()
    {
        _host.Api.On<GetOrderStateRequest, OrderState>("GetOrderState", _ => State("R-1", Request.ToString(), OrderExecutionReportStatus.ExecutionReportStatusFill, executed: 2, Stage("t-1", 250.5m, 2)));

        ExchangeOrder? order = await _ledger.FindOrderAsync(Key, Sber, "b-1", CancellationToken.None);

        order.ShouldBe(new ExchangeOrder("b-1", "R-1", ExchangeOrderState.Filled, 20m, null, 5010m, 0m, "RUB"));
        var read = (GetOrderStateRequest)_host.Api.Calls.Single(call => call.Method == "GetOrderState").Request;
        (read.AccountId, read.OrderId, read.OrderIdType).ShouldBe((AccountNumber, Request.ToString(), OrderIdType.Request));
    }

    // An order that never went has no request; T-Invest does not know an order of a day gone (ADR 0034): neither is read again.
    [Test]
    public async Task Order_without_a_request_is_unknown_without_a_call()
    {
        (await _ledger.FindOrderAsync(Key, Sber, "b-2", CancellationToken.None)).ShouldBeNull();
        (await _ledger.GetFillsAsync(Key, Sber, "b-2", CancellationToken.None)).ShouldBeEmpty();

        _host.Api.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Order_t_invest_does_not_know_is_unknown()
    {
        _host.Api.Fail("GetOrderState", StatusCode.NotFound, "50005");

        (await _ledger.FindOrderAsync(Key, Sber, "b-1", CancellationToken.None)).ShouldBeNull();
        (await _ledger.FindOrderByExchangeIdAsync(Key, Sber, "R-1", CancellationToken.None)).ShouldBeNull();
        (await _ledger.GetFillsAsync(Key, Sber, "b-1", CancellationToken.None)).ShouldBeEmpty();
        (await _ledger.GetOrderFillsAsync(Key, Sber, "R-1", CancellationToken.None)).ShouldBeEmpty();

        _host.Api.Calls.Count(call => call.Method == "GetOrderState").ShouldBe(4);
    }

    [Test]
    public async Task Order_is_read_by_its_exchange_id_and_told_by_its_request_id()
    {
        _host.Api.On<GetOrderStateRequest, OrderState>("GetOrderState", _ => State("R-1", Request.ToString(), OrderExecutionReportStatus.ExecutionReportStatusNew));

        (await _ledger.FindOrderByExchangeIdAsync(Key, Sber, "R-1", CancellationToken.None))!.ClientId.ShouldBe("b-1");

        var read = (GetOrderStateRequest)_host.Api.Calls.Single(call => call.Method == "GetOrderState").Request;
        (read.OrderId, read.OrderIdType).ShouldBe(("R-1", OrderIdType.Exchange));
    }

    [Test]
    public async Task Trades_of_an_order_are_its_stages()
    {
        _host.Api.On<GetOrderStateRequest, OrderState>("GetOrderState", _ => State("R-1", Request.ToString(), OrderExecutionReportStatus.ExecutionReportStatusFill, executed: 3, Stage("t-1", 250.5m, 2), Stage("t-2", 250.6m, 1)));

        IReadOnlyList<ExchangeFill> byClient = await _ledger.GetFillsAsync(Key, Sber, "b-1", CancellationToken.None);
        IReadOnlyList<ExchangeFill> byExchange = await _ledger.GetOrderFillsAsync(Key, Sber, "R-1", CancellationToken.None);

        byClient.Select(fill => (fill.ExecId, fill.ClientId, fill.Quantity)).ShouldBe([("t-1", "b-1", 20m), ("t-2", "b-1", 10m)]);
        byExchange.ShouldBe(byClient);
    }

    // The stop and the target of an entry are not orders of T-Invest in this change.
    [Test]
    public async Task Order_has_no_orders_of_its_own()
    {
        (await _ledger.GetOrdersOfParentAsync(Key, Sber, "b-1", CancellationToken.None)).ShouldBeEmpty();

        _host.Api.Calls.ShouldBeEmpty();
    }

    // Scenario "Ставка комиссии": a trial limit buy of whole lots for at least 100 000 ₽ at the last price; nothing is placed.
    [Test]
    public async Task Fee_rate_is_the_commission_of_a_trial_order_of_100000_roubles()
    {
        _host.Api
            .On<GetLastPricesRequest, GetLastPricesResponse>("GetLastPrices", _ => new GetLastPricesResponse { LastPrices = { new LastPrice { InstrumentUid = TInvestFixtures.SberUid, Price = 300m.ToQuotation() } } })
            .On<GetOrderPriceRequest, GetOrderPriceResponse>("GetOrderPrice", _ => new GetOrderPriceResponse { InitialOrderAmount = Money(100_000m, "rub"), DealCommission = Money(50m, "rub") });

        FeeSchedule fees = await _ledger.GetFeesAsync(Key, Sber, CancellationToken.None);

        fees.ShouldBe(new FeeSchedule(0.0005m, 0.0005m, MarketCategory.Moex));
        var trial = (GetOrderPriceRequest)_host.Api.Calls.Single(call => call.Method == "GetOrderPrice").Request;
        (trial.AccountId, trial.InstrumentId, trial.Direction, trial.Price.ToDecimal(), trial.Quantity).ShouldBe((AccountNumber, TInvestFixtures.SberUid, OrderDirection.Buy, 300m, 34L));
        _host.Api.Calls.ShouldNotContain(call => call.Method == "PostOrder");
    }

    // Without a last price of the share, as outside a session, the trial order has no amount: a refusal of the exchange, not a crash.
    [TestCase("no row")]
    [TestCase("no price")]
    [TestCase("zero")]
    public async Task Fee_rate_without_a_last_price_is_a_refusal_of_the_exchange(string told)
    {
        _host.Api
            .On<GetLastPricesRequest, GetLastPricesResponse>("GetLastPrices", _ => told switch
            {
                "no row" => new GetLastPricesResponse(),
                "no price" => new GetLastPricesResponse { LastPrices = { new LastPrice { InstrumentUid = TInvestFixtures.SberUid } } },
                _ => new GetLastPricesResponse { LastPrices = { new LastPrice { InstrumentUid = TInvestFixtures.SberUid, Price = 0m.ToQuotation() } } }
            })
            .On<GetOrderPriceRequest, GetOrderPriceResponse>("GetOrderPrice", _ => new GetOrderPriceResponse { InitialOrderAmount = Money(100_000m, "rub"), DealCommission = Money(50m, "rub") });

        ExchangeException exception = await Should.ThrowAsync<ExchangeException>(() => _ledger.GetFeesAsync(Key, Sber, CancellationToken.None));

        exception.ShouldBeOfType<ExchangeUnavailableException>();
        exception.Message.ShouldBe("T-Invest told no last price of SBER: the fee rate waits for one.");
        _host.Api.Calls.ShouldNotContain(call => call.Method == "GetOrderPrice");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Fee_rate_of_a_trial_order_without_an_amount_is_a_refusal_of_the_exchange(bool zero)
    {
        _host.Api
            .On<GetLastPricesRequest, GetLastPricesResponse>("GetLastPrices", _ => new GetLastPricesResponse { LastPrices = { new LastPrice { InstrumentUid = TInvestFixtures.SberUid, Price = 300m.ToQuotation() } } })
            .On<GetOrderPriceRequest, GetOrderPriceResponse>("GetOrderPrice", _ => zero
                ? new GetOrderPriceResponse { InitialOrderAmount = Money(0m, "rub"), DealCommission = Money(0m, "rub") }
                : new GetOrderPriceResponse { DealCommission = Money(50m, "rub") });

        ExchangeException exception = await Should.ThrowAsync<ExchangeException>(() => _ledger.GetFeesAsync(Key, Sber, CancellationToken.None));

        exception.ShouldBeOfType<ExchangeUnavailableException>();
        exception.Message.ShouldBe("T-Invest told no amount of the trial order of SBER: the fee rate waits for one.");
    }

    private static OrderState State(string id, string request, OrderExecutionReportStatus status, long executed = 0, params OrderStage[] stages)
    {
        return State(id, request, status, TInvestFixtures.SberUid, executed, stages);
    }

    private static OrderState State(string id, string request, OrderExecutionReportStatus status, string uid, long executed = 0, params OrderStage[] stages)
    {
        return new OrderState { OrderId = id, OrderRequestId = request, ExecutionReportStatus = status, InstrumentUid = uid, LotsRequested = 5, LotsExecuted = executed, Stages = { stages } };
    }

    private static OrderStage Stage(string id, decimal price, long lots)
    {
        return new OrderStage { TradeId = id, Price = Money(price, "rub"), Quantity = lots, ExecutionTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.Parse("2024-03-11T07:15:00Z")) };
    }

    private static MoneyValue Money(decimal value, string currency)
    {
        var quotation = value.ToQuotation();

        return new MoneyValue { Currency = currency, Units = quotation.Units, Nano = quotation.Nano };
    }
}