using System.Net;

using Microsoft.Extensions.Logging.Abstractions;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Infrastructure.Exchanges.Bybit;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

public class BybitLedgerTests
{
    private static readonly ExchangeKey Key = new ExchangeKey("key", "secret-value-never-shown");
    private static readonly Instrument Spot = new Instrument(MarketCategory.Spot, "BTCUSDT");
    private static readonly Instrument Linear = new Instrument(MarketCategory.Linear, "BTCUSDT");

    // What open orders lock is not free to trade with.
    [Test]
    public async Task Free_money_is_the_balance_less_what_orders_lock()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Wallet(Coin("USDT", "1000", "150"), Coin("BTC", "0.5", "0")));

        decimal free = await Ledger(handler).GetFreeAsync(Key, "USDT", CancellationToken.None);

        free.ShouldBe(850m);
        BybitRequests.Describe(handler.Requests.Single()).ShouldBe(BybitRequests.Describe("/v5/account/wallet-balance?accountType=UNIFIED"));
    }

    [Test]
    public async Task Coin_the_account_does_not_hold_is_no_money()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Wallet(Coin("BTC", "0.5", "0")));

        (await Ledger(handler).GetFreeAsync(Key, "USDT", CancellationToken.None)).ShouldBe(0m);
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    public async Task Hedge_mode_is_told_by_the_position_index(int index, bool hedge)
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Positions(Position(index, "", "0")));

        (await Ledger(handler).IsHedgeModeAsync(Key, Linear, CancellationToken.None)).ShouldBe(hedge);
        BybitRequests.Describe(handler.Requests.Single()).ShouldBe(BybitRequests.Describe("/v5/position/list?category=linear&symbol=BTCUSDT"));
    }

    [TestCase("Buy", "0.5", 0.5)]
    [TestCase("Sell", "0.5", -0.5)]
    [TestCase("", "0", 0)]
    public async Task Linear_holding_is_the_position_with_its_sign(string side, string size, decimal holding)
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Positions(Position(0, side, size)));

        (await Ledger(handler).GetHoldingAsync(Key, Linear, "BTC", CancellationToken.None)).ShouldBe(holding);
    }

    // Decision 2: in the one-way mode Bybit lists the row of the symbol with its leverage even without a position.
    [Test]
    public async Task Leverage_is_the_one_of_the_row_of_the_symbol_without_a_position()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Positions(Position(0, "", "0", leverage: "10")));

        (await Ledger(handler).GetLinearPositionAsync(Key, Linear, CancellationToken.None)).ShouldBe(new LinearPosition(10m, 0m));
    }

    // Review of add-idea-trade-leverage: the leverage and the position of the symbol come from one read of the position list.
    [Test]
    public async Task Linear_position_is_the_leverage_and_the_position_of_the_row_of_the_symbol_in_one_read()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Positions(Position(0, "Sell", "0.5", leverage: "10")));

        (await Ledger(handler).GetLinearPositionAsync(Key, Linear, CancellationToken.None)).ShouldBe(new LinearPosition(10m, -0.5m));
        BybitRequests.Describe(handler.Requests.Single()).ShouldBe(BybitRequests.Describe("/v5/position/list?category=linear&symbol=BTCUSDT"));
    }

    [Test]
    public async Task Spot_holding_is_the_coin_on_the_account()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Wallet(Coin("USDT", "1000", "0"), Coin("BTC", "0.4", "0")));

        (await Ledger(handler).GetHoldingAsync(Key, Spot, "BTC", CancellationToken.None)).ShouldBe(0.4m);
    }

    // The totals of an order are the truth of its fills: quantity, value and fee with its coin.
    [Test]
    public async Task Open_orders_carry_their_client_id_and_totals()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Orders(Order("c-1", "PartiallyFilled", "0.4")));

        IReadOnlyList<ExchangeOrder> orders = await Ledger(handler).GetOpenOrdersAsync(Key, Linear, CancellationToken.None);

        orders.ShouldBe([new ExchangeOrder("c-1", "x-c-1", ExchangeOrderState.Open, 0.4m, null, 40.4m, 0.0004m, "BTC")]);
        BybitRequests.Describe(handler.Requests.Single()).ShouldBe(BybitRequests.Describe("/v5/order/realtime?category=linear&symbol=BTCUSDT&openOnly=0&limit=50"));
    }

    // An order that finished leaves the open orders for the history.
    [TestCase("Filled", ExchangeOrderState.Filled)]
    [TestCase("Cancelled", ExchangeOrderState.Cancelled)]
    [TestCase("PartiallyFilledCanceled", ExchangeOrderState.Cancelled)]
    [TestCase("Rejected", ExchangeOrderState.Rejected)]
    public async Task Finished_order_is_found_in_the_history(string status, ExchangeOrderState state)
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Orders()).RespondJson(Orders(Order("c-1", status, "1")));

        ExchangeOrder? order = await Ledger(handler).FindOrderAsync(Key, Spot, "c-1", CancellationToken.None);

        order!.State.ShouldBe(state);
        BybitRequests.Describe(handler.Requests[1]).ShouldBe(BybitRequests.Describe("/v5/order/history?category=spot&symbol=BTCUSDT&orderLinkId=c-1"));
    }

    [Test]
    public async Task Order_the_exchange_does_not_know_is_not_found()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Orders()).RespondJson(Orders());

        (await Ledger(handler).FindOrderAsync(Key, Spot, "c-1", CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Fills_of_an_order_come_the_oldest_first()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(
            Executions(Execution("e-2", "0.6", "101", "0.06", "USDT", 1_700_000_060_000), Execution("e-1", "0.4", "100", "0.0004", "BTC", 1_700_000_000_000)));

        IReadOnlyList<ExchangeFill> fills = await Ledger(handler).GetFillsAsync(Key, Spot, "c-1", CancellationToken.None);

        fills.ShouldBe(
        [
            new ExchangeFill("e-1", "c-1", 0.4m, 100m, 0.0004m, "BTC", DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000), Spot, "x-c-1"),
            new ExchangeFill("e-2", "c-1", 0.6m, 101m, 0.06m, "USDT", DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_060_000), Spot, "x-c-1")
        ]);
        BybitRequests.Describe(handler.Requests.Single()).ShouldBe(BybitRequests.Describe("/v5/execution/list?category=spot&symbol=BTCUSDT&orderLinkId=c-1"));
    }

    // An order filled in many parts lists its fills on more than one page: every page is read.
    [Test]
    public async Task Fills_of_an_order_are_read_from_every_page()
    {
        using StubHttpHandler handler = new StubHttpHandler()
            .RespondJson(ExecutionPage("page-2", Execution("e-1", "0.4", "100", "0", "USDT", 1_700_000_000_000)))
            .RespondJson(ExecutionPage("", Execution("e-2", "0.6", "101", "0", "USDT", 1_700_000_060_000)));

        IReadOnlyList<ExchangeFill> fills = await Ledger(handler).GetFillsAsync(Key, Spot, "c-1", CancellationToken.None);

        fills.Select(fill => fill.ExecId).ShouldBe(["e-1", "e-2"]);
        BybitRequests.Describe(handler.Requests[1]).ShouldBe(BybitRequests.Describe("/v5/execution/list?category=spot&symbol=BTCUSDT&orderLinkId=c-1&cursor=page-2"));
    }

    // The stop and the target of an entry have no client id: they are found by the exchange's id, open or in the history.
    [Test]
    public async Task Order_is_found_by_the_id_of_the_exchange_in_the_open_orders_or_in_the_history()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Orders()).RespondJson(Orders(Order("", "Filled", "1")));

        ExchangeOrder? order = await Ledger(handler).FindOrderByExchangeIdAsync(Key, Linear, "x-stop", CancellationToken.None);

        order!.State.ShouldBe(ExchangeOrderState.Filled);
        BybitRequests.Describe(handler.Requests[0]).ShouldBe(BybitRequests.Describe("/v5/order/realtime?category=linear&symbol=BTCUSDT&orderId=x-stop"));
        BybitRequests.Describe(handler.Requests[1]).ShouldBe(BybitRequests.Describe("/v5/order/history?category=linear&symbol=BTCUSDT&orderId=x-stop"));
    }

    // The stop and the target of an entry: the open ones, and the ones the history of the symbol shows finished, that name the entry as their parent.
    [Test]
    public async Task Orders_of_a_parent_are_the_open_and_the_recent_ones_that_name_it()
    {
        using StubHttpHandler handler = new StubHttpHandler()
            .RespondJson(Orders(Child("x-tp", "New", "PartialTakeProfit", "c-1"), Child("x-other", "New", "PartialTakeProfit", "c-2")))
            .RespondJson(Orders(Child("x-sl", "Filled", "PartialStopLoss", "c-1"), Child("x-old", "Filled", "", "")));

        IReadOnlyList<ExchangeOrder> orders = await Ledger(handler).GetOrdersOfParentAsync(Key, Linear, "c-1", CancellationToken.None);

        orders.Select(order => (order.ExchangeId, order.State, order.StopKind)).ShouldBe([("x-tp", ExchangeOrderState.Open, StopOrderKind.TakeProfit), ("x-sl", ExchangeOrderState.Filled, StopOrderKind.StopLoss)]);
        BybitRequests.Describe(handler.Requests[0]).ShouldBe(BybitRequests.Describe("/v5/order/realtime?category=linear&symbol=BTCUSDT&openOnly=0&limit=50"));
        BybitRequests.Describe(handler.Requests[1]).ShouldBe(BybitRequests.Describe("/v5/order/history?category=linear&symbol=BTCUSDT&limit=50"));
    }

    [Test]
    public async Task Fills_of_an_order_are_found_by_the_id_of_the_exchange()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Executions(Execution("e-1", "0.4", "100", "0.0004", "BTC", 1_700_000_000_000)));

        IReadOnlyList<ExchangeFill> fills = await Ledger(handler).GetOrderFillsAsync(Key, Linear, "x-stop", CancellationToken.None);

        fills.Select(fill => fill.ExecId).ShouldBe(["e-1"]);
        BybitRequests.Describe(handler.Requests.Single()).ShouldBe(BybitRequests.Describe("/v5/execution/list?category=linear&symbol=BTCUSDT&orderId=x-stop"));
    }

    [Test]
    public async Task Fees_are_the_maker_and_taker_rates_of_the_account()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(
            """{"retCode":0,"retMsg":"OK","result":{"list":[{"symbol":"BTCUSDT","takerFeeRate":"0.00055","makerFeeRate":"0.0002"}]},"retExtInfo":{},"time":1676360412576}""");

        FeeSchedule fees = await Ledger(handler).GetFeesAsync(Key, Linear, CancellationToken.None);

        fees.ShouldBe(new FeeSchedule(0.0002m, 0.00055m, MarketCategory.Linear));
    }

    [Test]
    public async Task Rejected_key_is_told_apart()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson("""{"retCode":10003,"retMsg":"API key is invalid.","result":{},"retExtInfo":{},"time":1}""");

        await Should.ThrowAsync<ExchangeKeyRejectedException>(() => Ledger(handler).GetFreeAsync(Key, "USDT", CancellationToken.None));
    }

    [Test]
    public async Task Bybit_failure_is_unavailable()
    {
        using StubHttpHandler handler = new StubHttpHandler().Respond(HttpStatusCode.BadGateway);

        await Should.ThrowAsync<ExchangeUnavailableException>(() => Ledger(handler).GetOpenOrdersAsync(Key, Spot, CancellationToken.None));
    }

    // Bybit answers HTTP 403 when the IP of the requests went over its limit, and lets the IP in again after a while.
    [Test]
    public async Task Ip_ban_is_unavailable()
    {
        using StubHttpHandler handler = new StubHttpHandler().Respond(HttpStatusCode.Forbidden);

        await Should.ThrowAsync<ExchangeUnavailableException>(() => Ledger(handler).GetOpenOrdersAsync(Key, Spot, CancellationToken.None));
    }

    private static BybitLedger Ledger(StubHttpHandler handler)
    {
        return new BybitLedger(handler.CreateClient(), NullLoggerFactory.Instance, new BybitSettings());
    }

    private static string Wallet(params string[] coins)
    {
        return $$"""
                 {"retCode":0,"retMsg":"OK","result":{"list":[{"totalEquity":"0","accountType":"UNIFIED","totalWalletBalance":"0","totalMarginBalance":"0",
                 "totalAvailableBalance":"0","totalPerpUPL":"0","totalInitialMargin":"0","totalMaintenanceMargin":"0","accountIMRate":"0","accountMMRate":"0",
                 "accountLTV":"0","coin":[{{string.Join(",", coins)}}]}]},"retExtInfo":{},"time":1690872862481}
                 """;
    }

    private static string Coin(string coin, string walletBalance, string locked)
    {
        return $$"""{"coin":"{{coin}}","walletBalance":"{{walletBalance}}","usdValue":"0","equity":"{{walletBalance}}","locked":"{{locked}}","borrowAmount":"0"}""";
    }

    private static string Positions(params string[] positions)
    {
        return $$"""{"retCode":0,"retMsg":"OK","result":{"list":[{{string.Join(",", positions)}}],"nextPageCursor":"","category":"linear"},"retExtInfo":{},"time":1}""";
    }

    private static string Position(int index, string side, string size, string leverage = "1")
    {
        return $$"""
                 {"positionIdx":{{index}},"riskId":1,"riskLimitValue":"2000000","symbol":"BTCUSDT","side":"{{side}}","size":"{{size}}","avgPrice":"0",
                 "positionValue":"0","tradeMode":0,"positionStatus":"Normal","autoAddMargin":0,"adlRankIndicator":0,"leverage":"{{leverage}}","positionBalance":"0",
                 "markPrice":"100","liqPrice":"","bustPrice":"","positionMM":"0","positionIM":"0","tpslMode":"Full","takeProfit":"0","stopLoss":"0",
                 "trailingStop":"0","unrealisedPnl":"0","cumRealisedPnl":"0","seq":1,"isReduceOnly":false,"createdTime":"1676538056258",
                 "updatedTime":"1697673600012"}
                 """;
    }

    private static string Orders(params string[] orders)
    {
        return $$"""{"retCode":0,"retMsg":"OK","result":{"list":[{{string.Join(",", orders)}}],"nextPageCursor":"","category":"linear"},"retExtInfo":{},"time":1}""";
    }

    private static string Order(string clientId, string status, string filled)
    {
        return $$"""
                 {"orderId":"x-{{clientId}}","orderLinkId":"{{clientId}}","symbol":"BTCUSDT","price":"101","qty":"1","side":"Sell","orderStatus":"{{status}}",
                 "cumExecQty":"{{filled}}","cumExecValue":"40.4","cumExecFee":"0.0004","feeCurrency":"BTC","orderType":"Limit","timeInForce":"GTC","rejectReason":"EC_NoError",
                 "createdTime":"1684738540559","updatedTime":"1684738540561"}
                 """;
    }

    private static string Child(string exchangeId, string status, string stopType, string parent)
    {
        return $$"""
                 {"orderId":"{{exchangeId}}","orderLinkId":"","parentOrderLinkId":"{{parent}}","stopOrderType":"{{stopType}}","symbol":"BTCUSDT","price":"0","qty":"0.47","side":"Sell",
                 "orderStatus":"{{status}}","cumExecQty":"0","cumExecValue":"0","cumExecFee":"0","feeCurrency":"USDT","orderType":"Market","timeInForce":"IOC","rejectReason":"EC_NoError",
                 "createdTime":"1684738540559","updatedTime":"1684738540561"}
                 """;
    }

    private static string Executions(params string[] executions)
    {
        return ExecutionPage("", executions);
    }

    private static string ExecutionPage(string cursor, params string[] executions)
    {
        return $$"""{"retCode":0,"retMsg":"OK","result":{"list":[{{string.Join(",", executions)}}],"nextPageCursor":"{{cursor}}","category":"spot"},"retExtInfo":{},"time":1}""";
    }

    private static string Execution(string id, string quantity, string price, string fee, string feeCoin, long at)
    {
        return $$"""
                 {"symbol":"BTCUSDT","orderId":"x-c-1","orderLinkId":"c-1","side":"Buy","orderPrice":"100","orderQty":"1","leavesQty":"0","orderType":"Limit",
                 "execFee":"{{fee}}","feeCurrency":"{{feeCoin}}","execId":"{{id}}","execPrice":"{{price}}","execQty":"{{quantity}}","execType":"Trade",
                 "execValue":"0","execTime":"{{at}}","isMaker":true,"feeRate":"0.0001"}
                 """;
    }
}