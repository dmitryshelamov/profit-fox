using System.Net;

using Microsoft.Extensions.Logging.Abstractions;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges.Bybit;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

public class BybitOrderGatewayTests
{
    private static readonly ExchangeKey Key = new ExchangeKey("key", "secret-value-never-shown");
    private static readonly Instrument Spot = new Instrument(MarketCategory.Spot, "BTCUSDT");
    private static readonly OrderRequest Limit = new OrderRequest("c-1", OrderSide.Buy, OrderType.Limit, 99m, 1m, null);

    [Test]
    public async Task Accepted_order_carries_the_id_of_the_exchange()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Answer(0, "OK", """{"orderId":"x-1","orderLinkId":"c-1"}"""));

        OrderOutcome outcome = await Gateway(handler).PlaceAsync(Key, Spot, Limit, CancellationToken.None);

        outcome.ShouldBe(new OrderOutcome(OrderOutcomeKind.Accepted, "x-1"));
        handler.Requests.Single().AbsolutePath.ShouldBe("/v5/order/create");
    }

    // Scenario "Ответ на отправку потерян": the order resent with its client id is the order already on the exchange.
    [TestCase(110072)]
    [TestCase(170141)]
    public async Task Repeated_client_id_is_the_order_taken_already(int code)
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Answer(code, "Duplicate order link id", "{}"));

        (await Gateway(handler).PlaceAsync(Key, Spot, Limit, CancellationToken.None)).Kind.ShouldBe(OrderOutcomeKind.Accepted);
    }

    // Scenario "Лимит запросов".
    [TestCase(10006)]
    [TestCase(10016)]
    [TestCase(10000)]
    public async Task Rate_limit_and_failures_of_bybit_are_transient(int code)
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Answer(code, "Too many visits", "{}"));

        (await Gateway(handler).PlaceAsync(Key, Spot, Limit, CancellationToken.None)).Kind.ShouldBe(OrderOutcomeKind.Transient);
    }

    // Bybit answers HTTP 403 when the IP of the requests went over its limit, and lets the IP in again after a while.
    [Test]
    public async Task Ip_ban_is_transient()
    {
        using StubHttpHandler handler = new StubHttpHandler().Respond(HttpStatusCode.Forbidden);

        (await Gateway(handler).PlaceAsync(Key, Spot, Limit, CancellationToken.None)).Kind.ShouldBe(OrderOutcomeKind.Transient);
    }

    [Test]
    public async Task No_answer_is_transient()
    {
        using StubHttpHandler handler = new StubHttpHandler().Respond(HttpStatusCode.BadGateway);

        (await Gateway(handler).PlaceAsync(Key, Spot, Limit, CancellationToken.None)).Kind.ShouldBe(OrderOutcomeKind.Transient);
    }

    // Scenario "Нет денег": another attempt meets the same refusal.
    [TestCase(110007, "ab not enough for new order")]
    [TestCase(170131, "Insufficient balance.")]
    [TestCase(10003, "API key is invalid.")]
    [TestCase(170136, "Order quantity exceeded upper limit.")]
    public async Task Other_refusals_are_permanent_with_the_message(int code, string message)
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Answer(code, message, "{}"));

        (await Gateway(handler).PlaceAsync(Key, Spot, Limit, CancellationToken.None)).ShouldBe(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: message));
    }

    [Test]
    public async Task Spot_market_buy_by_amount_is_sized_in_the_quote_coin()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Answer(0, "OK", """{"orderId":"x-1","orderLinkId":"c-1"}"""));

        await Gateway(handler).PlaceAsync(Key, Spot, new OrderRequest("c-1", OrderSide.Buy, OrderType.Market, null, null, 100m), CancellationToken.None);

        handler.Bodies.Single().ShouldContain("\"marketUnit\":\"quoteCoin\"");
        handler.Bodies.Single().ShouldContain("\"qty\":\"100\"");
    }

    // Bybit reads the qty of a spot market buy in the quote coin unless told otherwise: 0.5 BTC would be 0.5 USDT.
    [TestCase(OrderSide.Buy)]
    [TestCase(OrderSide.Sell)]
    public async Task Spot_market_order_by_quantity_is_sized_in_the_base_coin(OrderSide side)
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Answer(0, "OK", """{"orderId":"x-1","orderLinkId":"c-1"}"""));

        await Gateway(handler).PlaceAsync(Key, Spot, new OrderRequest("c-1", side, OrderType.Market, null, 0.5m, null), CancellationToken.None);

        handler.Bodies.Single().ShouldContain("\"marketUnit\":\"baseCoin\"");
        handler.Bodies.Single().ShouldContain("\"qty\":\"0.5\"");
    }

    // The unit is a field of spot market orders only; a limit order and linear always go by quantity.
    [TestCase(MarketCategory.Spot, OrderType.Limit)]
    [TestCase(MarketCategory.Linear, OrderType.Market)]
    [TestCase(MarketCategory.Linear, OrderType.Limit)]
    public async Task Order_other_than_spot_market_carries_no_unit(MarketCategory category, OrderType type)
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Answer(0, "OK", """{"orderId":"x-1","orderLinkId":"c-1"}"""));

        await Gateway(handler).PlaceAsync(
            Key,
            new Instrument(category, "BTCUSDT"),
            new OrderRequest("c-1", OrderSide.Buy, type, type == OrderType.Limit ? 99m : null, 0.5m, null),
            CancellationToken.None);

        handler.Bodies.Single().ShouldNotContain("marketUnit");
    }

    [Test]
    public async Task Cancel_is_sent_by_the_client_id()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Answer(0, "OK", """{"orderId":"x-1","orderLinkId":"c-1"}"""));

        (await Gateway(handler).CancelAsync(Key, Spot, "c-1", CancellationToken.None)).Kind.ShouldBe(OrderOutcomeKind.Accepted);

        handler.Requests.Single().AbsolutePath.ShouldBe("/v5/order/cancel");
        handler.Bodies.Single().ShouldContain("\"orderLinkId\":\"c-1\"");
    }

    // An order that filled or was cancelled meanwhile has nothing to cancel; its end comes with its events.
    [TestCase(110001)]
    [TestCase(170213)]
    public async Task Cancel_of_a_finished_order_is_done_already(int code)
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Answer(code, "Order does not exist.", "{}"));

        (await Gateway(handler).CancelAsync(Key, Spot, "c-1", CancellationToken.None)).Kind.ShouldBe(OrderOutcomeKind.AlreadyDone);
    }

    private static BybitOrderGateway Gateway(StubHttpHandler handler)
    {
        return new BybitOrderGateway(handler.CreateClient(), NullLoggerFactory.Instance, new BybitSettings());
    }

    private static string Answer(int code, string message, string result)
    {
        return $$"""{"retCode":{{code}},"retMsg":"{{message}}","result":{{result}},"retExtInfo":{},"time":1672211918471}""";
    }
}