using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Infrastructure.Clients.Bybit;
using ProfitFox.Tests.Shared.Logging;

namespace ProfitFox.Tests.Integration.Infrastructure.Clients.Bybit;

/// <summary>
///     Change take-idea-trades, tasks 5.3 and 5.4 (design.md, decisions 3, 4, 5 and 7): the reads and the writes of the client
///     that trades, over a stub of HTTP with answers in the form of Bybit — the parameters of each request, the reading of the
///     position, the fills and the orders, every page of the fills, the bodies of the orders field for field, and the table
///     of the answers of decision 4. No request reaches Bybit.
/// </summary>
public sealed class BybitTradingClientTests
{
    private const string Key = "XvK3pTq9AbCdEf1234";
    private const string Secret = "s3cr3t-value-never-shown";
    private static readonly ExchangeCredentials Credentials = new ExchangeCredentials(Key, Secret);
    private static readonly DateTimeOffset Entered = DateTimeOffset.FromUnixTimeMilliseconds(1795874400123);

    private StubHttpHandler _bybit = null!;
    private ListLogger _log = null!;

    [SetUp]
    public void Stub()
    {
        _bybit = new StubHttpHandler();
        _log = new ListLogger();
    }

    [TearDown]
    public void Drop()
    {
        _bybit.Dispose();
    }

    [Test]
    public async Task The_positions_of_the_account_are_read_by_the_settle_coin_with_stop_target_leverage_and_mode()
    {
        _bybit.Json(BybitSamples.List("linear", "", BybitSamples.Position, BybitSamples.EmptyPosition));

        IReadOnlyList<ExchangePosition> positions = await Client().PositionsAsync(Credentials, null, CancellationToken.None);

        Assert.That(positions, Is.EqualTo(new[]
        {
            new ExchangePosition("SOLUSDT", TradeSide.Long, 0.4m, 237.4m, 231.38m, 245.54m, 3m, true, 0.004m, 237.41m),
            new ExchangePosition("XRPUSDT", null, 0m, null, null, null, 10m, true, null, 1.4126m)
        }));
        (Uri uri, Dictionary<string, string> headers) = _bybit.Requests.Single();
        Assert.That(uri.AbsolutePath, Is.EqualTo("/v5/position/list"));
        Assert.That(uri.Query, Does.Contain("category=linear").And.Contain("settleCoin=USDT"));
        Assert.That(headers["X-BAPI-API-KEY"], Is.EqualTo(Key));
    }

    [Test]
    public async Task The_positions_of_a_pair_are_read_by_its_symbol_and_tell_the_hedge_mode()
    {
        _bybit.Json(BybitSamples.List("linear", "", BybitSamples.HedgePosition));

        IReadOnlyList<ExchangePosition> positions = await Client().PositionsAsync(Credentials, "SOLUSDT", CancellationToken.None);

        Assert.That(positions.Single().OneWay, Is.False);
        Assert.That(_bybit.Requests.Single().Uri.Query, Does.Contain("symbol=SOLUSDT").And.Not.Contain("settleCoin"));
    }

    // Design.md, decision 5: the kind of the fill, the order it came from, the fee — of funding, what was paid.
    [Test]
    public async Task The_fills_are_read_from_the_moment_with_their_kind_stop_order_and_fee_through_every_page()
    {
        _bybit.Json(BybitSamples.List("linear", "page-2", BybitSamples.EntryFill, BybitSamples.StopFill))
            .Json(BybitSamples.List("linear", "", BybitSamples.BustFill, BybitSamples.FundingFill));

        IReadOnlyList<ExchangeFill> fills = await Client().FillsAsync(Credentials, Entered.AddMinutes(-1), CancellationToken.None);

        Assert.That(fills, Is.EqualTo(new[]
        {
            new ExchangeFill("e-0001", "SOLUSDT", BybitSamples.LinkId, TradeSide.Long, 237.4m, 0.4m, 0.0522m, ExchangeFillType.Trade, ExchangeStopType.None, Entered),
            new ExchangeFill("e-0002", "SOLUSDT", null, TradeSide.Short, 231.38m, 0.4m, 0.0509m, ExchangeFillType.Trade, ExchangeStopType.StopLoss, DateTimeOffset.FromUnixTimeMilliseconds(1795878000456)),
            new ExchangeFill("e-0003", "SOLUSDT", null, TradeSide.Short, 158.3m, 0.4m, 0m, ExchangeFillType.Liquidation, ExchangeStopType.None, DateTimeOffset.FromUnixTimeMilliseconds(1795878000789)),
            new ExchangeFill("e-0004", "SOLUSDT", null, TradeSide.Long, 237.5m, 0.4m, -0.05m, ExchangeFillType.Funding, ExchangeStopType.None, DateTimeOffset.FromUnixTimeMilliseconds(1795881600000))
        }));
        Assert.That(_bybit.Requests.Select(request => request.Uri.AbsolutePath), Is.EqualTo(new[] { "/v5/execution/list", "/v5/execution/list" }));
        Assert.That(_bybit.Requests[0].Uri.Query, Does.Contain("category=linear").And.Contain($"startTime={Entered.AddMinutes(-1).ToUnixTimeMilliseconds()}").And.Not.Contain("cursor"));
        Assert.That(_bybit.Requests[1].Uri.Query, Does.Contain("cursor=page-2"));
    }

    // Review, finding 4: with a start alone Bybit gives the seven days after it; a read from ten days back goes in two
    // windows, each with its start and end, up to a minute past now (N2), and a fill told in both is kept once.
    [Test]
    public async Task Fills_from_further_back_than_seven_days_are_read_window_by_window_up_to_now()
    {
        var now = new DateTimeOffset(2026, 11, 28, 14, 0, 0, TimeSpan.Zero);
        _bybit.Json(BybitSamples.List("linear", "", BybitSamples.EntryFill)).Json(BybitSamples.List("linear", "", BybitSamples.EntryFill, BybitSamples.StopFill));

        IReadOnlyList<ExchangeFill> fills = await Client(new FakeTimeProvider(now)).FillsAsync(Credentials, now.AddDays(-10), CancellationToken.None);

        Assert.That(fills.Select(fill => fill.ExecId), Is.EqualTo(new[] { "e-0001", "e-0002" }));
        Assert.That(_bybit.Requests.Select(request => request.Uri.Query), Is.EqualTo(new[]
        {
            $"?category=linear&startTime={now.AddDays(-10).ToUnixTimeMilliseconds()}&endTime={now.AddDays(-3).ToUnixTimeMilliseconds()}&limit=100",
            $"?category=linear&startTime={now.AddDays(-3).ToUnixTimeMilliseconds()}&endTime={now.AddMinutes(1).ToUnixTimeMilliseconds()}&limit=100"
        }).Using<string>((actual, expected) => Parameters(actual).SetEquals(Parameters(expected)) ? 0 : 1));
    }

    // 12.16 (N2): a start ahead of the clock of the Worker still reads one window whose end is after its start.
    [Test]
    public async Task A_start_ahead_of_the_clock_reads_one_window_that_ends_after_it_starts()
    {
        var now = new DateTimeOffset(2026, 11, 28, 14, 0, 0, TimeSpan.Zero);
        _bybit.Json(BybitSamples.List("linear", "", BybitSamples.EntryFill));

        IReadOnlyList<ExchangeFill> fills = await Client(new FakeTimeProvider(now)).FillsAsync(Credentials, now.AddMinutes(2), CancellationToken.None);

        Assert.That(fills, Has.Count.EqualTo(1));
        var query = Parameters(_bybit.Requests.Single().Uri.Query).Select(pair => pair.Split('=')).Where(pair => pair[0] is "startTime" or "endTime").ToDictionary(pair => pair[0], pair => long.Parse(pair[1]));
        Assert.That(query["endTime"], Is.GreaterThan(query["startTime"]));
    }

    private static HashSet<string> Parameters(string query)
    {
        return [.. query.TrimStart('?').Split('&')];
    }

    [Test]
    public async Task An_order_is_looked_for_among_the_open_then_in_the_history_by_its_link_id()
    {
        _bybit.Json(BybitSamples.List("linear", "")).Json(BybitSamples.List("linear", "", BybitSamples.Order));

        ExchangeOrderUpdate? order = await Client().OrderAsync(Credentials, "SOLUSDT", BybitSamples.LinkId, CancellationToken.None);

        Assert.That(order, Is.EqualTo(new ExchangeOrderUpdate("SOLUSDT", BybitSamples.LinkId, "5f1a9c2e-0001", ExchangeOrderState.Filled, 0.4m, null)));
        Assert.That(_bybit.Requests.Select(request => request.Uri.AbsolutePath), Is.EqualTo(new[] { "/v5/order/realtime", "/v5/order/history" }));
        Assert.That(_bybit.Requests.Select(request => request.Uri.Query), Has.All.Contain($"orderLinkId={BybitSamples.LinkId}"));
    }

    [Test]
    public async Task An_order_found_nowhere_is_none_and_a_refused_one_tells_why()
    {
        _bybit.Json(BybitSamples.List("linear", "")).Json(BybitSamples.List("linear", "")).Json(BybitSamples.List("linear", "", BybitSamples.RejectedOrder));

        Assert.That(await Client().OrderAsync(Credentials, "SOLUSDT", BybitSamples.LinkId, CancellationToken.None), Is.Null);
        ExchangeOrderUpdate rejected = (await Client().OrderAsync(Credentials, "SOLUSDT", BybitSamples.LinkId, CancellationToken.None))!;
        Assert.That((rejected.State, rejected.Reason), Is.EqualTo((ExchangeOrderState.Rejected, "EC_InsufficientMargin")));
    }

    [Test]
    public async Task The_closed_result_of_the_pair_sums_what_Bybit_counted()
    {
        const string Closed =
            """{"symbol":"SOLUSDT","orderId":"5f1a9c2e-0002","side":"Sell","qty":"0.4","orderPrice":"219.81","orderType":"Market","execType":"Trade","closedSize":"0.4","cumEntryValue":"94.96","avgEntryPrice":"237.4","cumExitValue":"92.552","avgExitPrice":"231.38","closedPnl":"-2.5111","fillCount":"1","leverage":"3","openFee":"0.0522","closeFee":"0.0509","createdTime":"1795878000456","updatedTime":"1795878000500"}""";
        _bybit.Json(BybitSamples.List("linear", "", Closed));

        ExchangeClosedResult? closed = await Client().ClosedAsync(Credentials, "SOLUSDT", Entered, CancellationToken.None);

        Assert.That(closed, Is.EqualTo(new ExchangeClosedResult(-2.5111m, 231.38m, 0.4m, DateTimeOffset.FromUnixTimeMilliseconds(1795878000500))));
        Assert.That(_bybit.Requests.Single().Uri.AbsolutePath, Is.EqualTo("/v5/position/closed-pnl"));
        Assert.That(_bybit.Requests.Single().Uri.Query, Does.Contain("symbol=SOLUSDT").And.Contain($"startTime={Entered.ToUnixTimeMilliseconds()}"));
    }

    // Design.md, decision 4: the body of the entry order field for field.
    [Test]
    public async Task The_entry_is_a_market_order_of_the_one_way_mode_with_the_stop_and_the_target_of_the_whole_position()
    {
        _bybit.Json(BybitSamples.Ok("""{"orderId":"5f1a9c2e-0001","orderLinkId":"t0a1b2c3d4e5f6g7h8i9j0k1l2-1"}"""));

        ExchangeAnswer answer = await Client().PlaceOrderAsync(Credentials, new ExchangeOrderRequest("SOLUSDT", TradeSide.Long, 0.4m, BybitSamples.LinkId, 231.38m, 245.54m, false), CancellationToken.None);

        Assert.That(answer, Is.EqualTo(new ExchangeAnswer(ExchangeAnswerKind.Accepted, OrderId: "5f1a9c2e-0001")));
        Assert.That(_bybit.Requests.Single().Uri.AbsolutePath, Is.EqualTo("/v5/order/create"));
        Assert.That(Body(), Is.EqualTo(new Dictionary<string, string>
        {
            ["category"] = "linear",
            ["symbol"] = "SOLUSDT",
            ["side"] = "Buy",
            ["orderType"] = "Market",
            ["qty"] = "0.4",
            ["positionIdx"] = "0",
            ["orderLinkId"] = BybitSamples.LinkId,
            ["stopLoss"] = "231.38",
            ["takeProfit"] = "245.54",
            ["slTriggerBy"] = "LastPrice",
            ["tpTriggerBy"] = "LastPrice",
            ["tpslMode"] = "Full"
        }));
    }

    // Spec idea-trades «Сделка без цели».
    [Test]
    public async Task An_entry_without_a_target_carries_only_the_stop()
    {
        _bybit.Json(BybitSamples.Ok("""{"orderId":"o","orderLinkId":"l"}"""));

        await Client().PlaceOrderAsync(Credentials, new ExchangeOrderRequest("XRPUSDT", TradeSide.Short, 70m, BybitSamples.LinkId, 1.4549m, null, false), CancellationToken.None);

        Assert.That(Body().Keys, Is.EquivalentTo(new[] { "category", "symbol", "side", "orderType", "qty", "positionIdx", "orderLinkId", "stopLoss", "slTriggerBy", "tpslMode" }));
        Assert.That(Body()["side"], Is.EqualTo("Sell"));
    }

    [Test]
    public async Task The_close_is_a_market_order_of_the_other_side_that_only_makes_the_position_smaller()
    {
        _bybit.Json(BybitSamples.Ok("""{"orderId":"o","orderLinkId":"l"}"""));

        await Client().PlaceOrderAsync(Credentials, new ExchangeOrderRequest("SOLUSDT", TradeSide.Short, 0.4m, BybitSamples.LinkId, null, null, true), CancellationToken.None);

        Assert.That(Body(), Is.EqualTo(new Dictionary<string, string>
        {
            ["category"] = "linear",
            ["symbol"] = "SOLUSDT",
            ["side"] = "Sell",
            ["orderType"] = "Market",
            ["qty"] = "0.4",
            ["positionIdx"] = "0",
            ["orderLinkId"] = BybitSamples.LinkId,
            ["reduceOnly"] = "true"
        }));
    }

    [Test]
    public async Task The_leverage_of_both_sides_and_the_stop_of_the_position_are_set_by_their_calls()
    {
        _bybit.Json(BybitSamples.Ok("{}")).Json(BybitSamples.Ok("{}"));

        Assert.That((await Client().SetLeverageAsync(Credentials, "SOLUSDT", 3, CancellationToken.None)).Kind, Is.EqualTo(ExchangeAnswerKind.Accepted));
        Assert.That(Body(), Is.EqualTo(new Dictionary<string, string> { ["category"] = "linear", ["symbol"] = "SOLUSDT", ["buyLeverage"] = "3", ["sellLeverage"] = "3" }));
        Assert.That((await Client().SetStopAsync(Credentials, "SOLUSDT", 231.38m, CancellationToken.None)).Kind, Is.EqualTo(ExchangeAnswerKind.Accepted));
        Assert.That(
            Body(1),
            Is.EqualTo(new Dictionary<string, string> { ["category"] = "linear", ["symbol"] = "SOLUSDT", ["positionIdx"] = "0", ["stopLoss"] = "231.38", ["slTriggerBy"] = "LastPrice", ["tpslMode"] = "Full" }));
        Assert.That(_bybit.Requests.Select(request => request.Uri.AbsolutePath), Is.EqualTo(new[] { "/v5/position/set-leverage", "/v5/position/trading-stop" }));
    }

    // Design.md, decision 4: the table of the answers.
    [TestCase(110072, "OrderLinkedID is duplicate", ExchangeAnswerKind.Duplicate)]
    [TestCase(110043, "leverage not modified", ExchangeAnswerKind.Unchanged)]
    [TestCase(10000, "Server Timeout", ExchangeAnswerKind.Temporary)]
    [TestCase(10006, "Too many visits!", ExchangeAnswerKind.Temporary)]
    [TestCase(10016, "Server error", ExchangeAnswerKind.Temporary)]
    [TestCase(110007, "ab not enough for new order", ExchangeAnswerKind.Refused)]
    [TestCase(10001, "Params error.", ExchangeAnswerKind.Refused)]
    public async Task A_code_of_Bybit_answers_by_the_table(int code, string reason, ExchangeAnswerKind kind)
    {
        _bybit.Json(BybitSamples.Error(code, reason));

        ExchangeAnswer answer = await Client().PlaceOrderAsync(Credentials, new ExchangeOrderRequest("SOLUSDT", TradeSide.Long, 0.4m, BybitSamples.LinkId, 231.38m, null, false), CancellationToken.None);

        // The package reads the limit of requests as its own kind of error, without the code of Bybit.
        Assert.That(answer.Kind, Is.EqualTo(kind));
        Assert.That(answer.Reason, Does.EndWith(reason));
        Assert.That(answer.Code, code == 10006 ? Is.Null.Or.EqualTo("10006") : Is.EqualTo(code.ToString()));
    }

    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.BadGateway)]
    public async Task Http_403_and_5xx_are_for_now(HttpStatusCode status)
    {
        _bybit.Status(status);

        Assert.That((await Client().SetLeverageAsync(Credentials, "SOLUSDT", 3, CancellationToken.None)).Kind, Is.EqualTo(ExchangeAnswerKind.Temporary));
        Assert.That(_bybit.Requests, Has.Count.EqualTo(1), "one attempt");
    }

    [Test]
    public async Task No_answer_is_for_now()
    {
        _bybit.Answer(() => throw new HttpRequestException("connection reset"));

        Assert.That((await Client().SetStopAsync(Credentials, "SOLUSDT", 231.38m, CancellationToken.None)).Kind, Is.EqualTo(ExchangeAnswerKind.Temporary));
    }

    [Test]
    public void A_key_Bybit_does_not_take_throws_on_a_write_and_on_a_read()
    {
        _bybit.Json(BybitSamples.Error(10003, "API key is invalid.")).Json(BybitSamples.Error(10003, "API key is invalid.")).Status(HttpStatusCode.ServiceUnavailable);

        Assert.ThrowsAsync<ExchangeKeyRejectedException>(() => Client().SetLeverageAsync(Credentials, "SOLUSDT", 3, CancellationToken.None));
        Assert.ThrowsAsync<ExchangeKeyRejectedException>(() => Client().PositionsAsync(Credentials, null, CancellationToken.None));
        Assert.ThrowsAsync<ExchangeUnavailableException>(() => Client().FillsAsync(Credentials, Entered, CancellationToken.None));
    }

    // Spec trade-risk: the secret and the signature never reach the journal, even with the raw answers at the level of debug.
    [Test]
    public async Task The_journal_has_the_answers_and_neither_the_secret_nor_the_signature()
    {
        _bybit.Json(BybitSamples.Ok("""{"orderId":"5f1a9c2e-0001","orderLinkId":"t0a1b2c3d4e5f6g7h8i9j0k1l2-1"}""")).Json(BybitSamples.Error(110007, "ab not enough for new order"));

        await Client().PlaceOrderAsync(Credentials, new ExchangeOrderRequest("SOLUSDT", TradeSide.Long, 0.4m, BybitSamples.LinkId, 231.38m, null, false), CancellationToken.None);
        await Client().PlaceOrderAsync(Credentials, new ExchangeOrderRequest("SOLUSDT", TradeSide.Long, 0.4m, BybitSamples.LinkId, 231.38m, null, false), CancellationToken.None);

        string journal = string.Join("\n", _log.Entries.Select(entry => entry.Message));
        string signatures = string.Join("\n", _bybit.Requests.Select(request => request.Headers["X-BAPI-SIGN"]));
        Assert.That(_log.Entries.Select(entry => entry.Level), Does.Contain(LogLevel.Debug).And.Contain(LogLevel.Warning));
        Assert.That(journal, Does.Contain("5f1a9c2e-0001").And.Contain("ab not enough for new order"));
        Assert.That(journal, Does.Not.Contain(Secret));
        foreach (string signature in signatures.Split('\n'))
        {
            Assert.That(journal, Does.Not.Contain(signature));
        }

        Assert.That(Credentials.ToString(), Does.Not.Contain(Secret));
    }

    private Dictionary<string, string> Body(int index = 0)
    {
        using var document = JsonDocument.Parse(_bybit.Bodies[index].Body);
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText());
    }

    private BybitTradingClient Client(TimeProvider? clock = null)
    {
        return new BybitTradingClient(_bybit.Client(), new BybitSettings(), new ListLogger<BybitTradingClient>(_log), clock ?? new FakeTimeProvider(Entered.AddHours(2)));
    }
}
