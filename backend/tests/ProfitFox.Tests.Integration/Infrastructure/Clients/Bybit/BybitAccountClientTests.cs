using System.Net;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.ValueObjects.Accounts;
using ProfitFox.Infrastructure.Clients.Bybit;
using ProfitFox.Tests.Shared.Logging;

namespace ProfitFox.Tests.Integration.Infrastructure.Clients.Bybit;

/// <summary>
///     Change add-exchange-accounts, tasks 4.1–4.3 (design.md, decisions 4, 6 and 7): the client of Bybit over a stub of HTTP —
///     the path, the key and the signature in the headers, the permissions of the key, the refusals of Bybit by the table of
///     decision 6 and the free money of the unified account. No request reaches Bybit.
/// </summary>
public sealed class BybitAccountClientTests
{
    private const string Key = "XvK3pTq9AbCdEf1234";
    private const string Secret = "s3cr3t-value-never-shown";

    private const string Trading = """{"ContractTrade":["Order","Position"],"Spot":["SpotTrade"],"Wallet":["AccountTransfer","SubMemberTransfer"],"Options":[],"Derivatives":[],"CopyTrading":[],"BlockTrade":[],"Exchange":[],"NFT":[],"Affiliate":[]}""";

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
    public async Task The_permissions_are_asked_at_query_api_with_the_key_and_a_signature_in_the_headers()
    {
        _bybit.Json(KeyInfo(Trading));

        ExchangeKeyInfo key = await Client().InspectAsync(Key, Secret, CancellationToken.None);

        Assert.That(key, Is.EqualTo(key with { Uid = 104857321, ReadOnly = false, Unified = true }));
        Assert.That(key.Wallet, Is.EqualTo(new[] { "AccountTransfer", "SubMemberTransfer" }));
        Assert.That(key.ContractTrade, Is.EqualTo(new[] { "Order", "Position" }));
        (Uri uri, Dictionary<string, string> headers) = _bybit.Requests.Single();
        Assert.That(uri.Host, Is.EqualTo("api.bybit.com"), "no address set is the live Bybit");
        Assert.That(uri.AbsolutePath, Is.EqualTo("/v5/user/query-api"));
        Assert.That(headers["X-BAPI-API-KEY"], Is.EqualTo(Key));
        Assert.That(headers["X-BAPI-SIGN"], Has.Length.EqualTo(64), "HMAC-SHA256 in hex");
        Assert.That(headers.Keys, Does.Contain("X-BAPI-TIMESTAMP"));
        Assert.That(string.Join(";", headers.Values) + uri, Does.Not.Contain(Secret));
    }

    [Test]
    public async Task The_address_of_the_settings_takes_the_calls_and_each_call_builds_its_own_client()
    {
        _bybit.Json(KeyInfo(Trading)).Json(KeyInfo(Trading, readOnly: 1, uta: 0, uid: 104850011));
        BybitAccountClient client = Client(new BybitSettings("http://127.0.0.1:5296"));

        await client.InspectAsync(Key, Secret, CancellationToken.None);
        ExchangeKeyInfo second = await client.InspectAsync("other-key", Secret, CancellationToken.None);

        Assert.That(_bybit.Requests.Select(request => request.Uri.Authority), Is.EqualTo(new[] { "127.0.0.1:5296", "127.0.0.1:5296" }));
        Assert.That(_bybit.Requests[1].Headers["X-BAPI-API-KEY"], Is.EqualTo("other-key"));
        Assert.That((second.Uid, second.ReadOnly, second.Unified), Is.EqualTo((104850011L, true, false)));
    }

    // Design.md, decision 6: the codes of a key Bybit does not take; spec exchange-accounts «Bybit не знает ключ».
    [TestCase(10003, "API key is invalid.")]
    [TestCase(10004, "Error sign, please check your signature generation algorithm.")]
    [TestCase(10005, "Permission denied, please check your API key permissions.")]
    [TestCase(10010, "Unmatched IP, please check your API key's bound IP addresses.")]
    [TestCase(33004, "Your api key has expired.")]
    public void A_key_Bybit_does_not_take_is_rejected_with_the_reason_of_Bybit(int code, string reason)
    {
        _bybit.Json(Error(code, reason));

        ExchangeKeyRejectedException? rejected = Assert.ThrowsAsync<ExchangeKeyRejectedException>(() => Client().InspectAsync(Key, Secret, CancellationToken.None));

        Assert.That(rejected!.Message, Is.EqualTo(reason));
    }

    [Test]
    public void Http_403_is_Bybit_unavailable()
    {
        _bybit.Status(HttpStatusCode.Forbidden);

        Assert.ThrowsAsync<ExchangeUnavailableException>(() => Client().InspectAsync(Key, Secret, CancellationToken.None));
    }

    [Test]
    public void Http_500_is_Bybit_unavailable_after_one_attempt()
    {
        _bybit.Status(HttpStatusCode.InternalServerError);

        Assert.ThrowsAsync<ExchangeUnavailableException>(() => Client().InspectAsync(Key, Secret, CancellationToken.None));
        Assert.That(_bybit.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public void No_answer_is_Bybit_unavailable()
    {
        _bybit.Answer(() => throw new HttpRequestException("connection refused"));

        Assert.ThrowsAsync<ExchangeUnavailableException>(() => Client().InspectAsync(Key, Secret, CancellationToken.None));
    }

    [Test]
    public void The_limit_of_requests_is_Bybit_unavailable()
    {
        _bybit.Json(Error(10006, "Too many visits!"));

        Assert.ThrowsAsync<ExchangeUnavailableException>(() => Client().InspectAsync(Key, Secret, CancellationToken.None));
    }

    // Design.md, decision 6: another refusal, such as 10002 — the clocks are apart — is an error of the exchange with the
    // reason of Bybit, not a refused key and not an unavailable exchange.
    [TestCase(10002, "invalid request, please check your server timestamp or recv_window param")]
    [TestCase(10001, "Params error.")]
    public void Another_code_is_an_error_of_the_exchange_with_the_reason_of_Bybit(int code, string reason)
    {
        _bybit.Json(Error(code, reason));

        ExchangeException? refused = Assert.ThrowsAsync<ExchangeException>(() => Client().InspectAsync(Key, Secret, CancellationToken.None));

        Assert.That(refused, Is.Not.InstanceOf<ExchangeKeyRejectedException>().And.Not.InstanceOf<ExchangeUnavailableException>());
        Assert.That(refused!.Message, Is.EqualTo(reason));
    }

    // Spec exchange-accounts «Секрет в журнале»: the refusal is in the journal, the secret and the answer of query-api are not.
    [Test]
    public void The_journal_tells_the_refusal_without_the_secret_or_the_answer_of_query_api()
    {
        string answer = Error(10003, "API key is invalid.").Replace("\"result\":{}", $$"""
            "result":{"apiKey":"{{Key}}","secret":"{{Secret}}","note":"bot-note"}
            """, StringComparison.Ordinal);
        _bybit.Json(answer);

        Assert.ThrowsAsync<ExchangeKeyRejectedException>(() => Client().InspectAsync(Key, Secret, CancellationToken.None));

        (LogLevel level, string message) = _log.Entries.Single();
        Assert.That(level, Is.EqualTo(LogLevel.Warning));
        Assert.That(message, Does.Contain("query-api").And.Contain("10003").And.Contain("API key is invalid."));
        Assert.That(message, Does.Not.Contain(Secret).And.Not.Contain("bot-note").And.Not.Contain("retCode"));
    }

    // Design.md, decision 7: spec exchange-accounts «Баланс показан».
    [Test]
    public async Task The_free_money_is_the_available_balance_of_the_unified_account()
    {
        _bybit.Json(Balance("1250.40"));

        decimal free = await Client().FreeAsync(Key, Secret, CancellationToken.None);

        Assert.That(free, Is.EqualTo(1250.40m));
        (Uri uri, Dictionary<string, string> headers) = _bybit.Requests.Single();
        Assert.That(uri.AbsolutePath, Is.EqualTo("/v5/account/wallet-balance"));
        Assert.That(uri.Query, Does.Contain("accountType=UNIFIED"));
        Assert.That(headers["X-BAPI-API-KEY"], Is.EqualTo(Key));
    }

    [Test]
    public async Task An_account_Bybit_lists_no_unified_wallet_for_has_nothing_free()
    {
        _bybit.Json("""{"retCode":0,"retMsg":"OK","result":{"list":[]},"retExtInfo":{},"time":1732788000000}""");

        Assert.That(await Client().FreeAsync(Key, Secret, CancellationToken.None), Is.Zero);
    }

    // Found by task 7.2: spec exchange-accounts «Изолированная маржа». In the isolated margin Bybit sends the fields of the
    // whole account empty; the free money is that of USDT of the unified wallet by the formula of Bybit for the isolated
    // margin, an empty bonus nought. The empty string becomes no number in Bybit.Net 7.9.0, not an error of parsing.
    [Test]
    public async Task In_the_isolated_margin_the_free_money_is_counted_from_USDT_of_the_unified_wallet()
    {
        _bybit.Json(Wallet("", Coin("BTC", wallet: "0.02", positionIm: "0", orderIm: "0"), Coin("USDT", wallet: "1500.50", positionIm: "200.10", orderIm: "50", locked: "0", bonus: "")));

        decimal free = await Client().FreeAsync(Key, Secret, CancellationToken.None);

        Assert.That(free, Is.EqualTo(1250.40m));
    }

    [Test]
    public async Task Without_the_field_of_the_whole_account_the_free_money_is_counted_the_same_way()
    {
        _bybit.Json(Wallet(null, Coin("USDT", wallet: "1000", positionIm: "100", orderIm: "", locked: "25", bonus: "5")));

        Assert.That(await Client().FreeAsync(Key, Secret, CancellationToken.None), Is.EqualTo(870m));
    }

    // No USDT in the unified wallet: no money for the USDT perpetuals.
    [Test]
    public async Task A_unified_wallet_without_USDT_has_nothing_free()
    {
        _bybit.Json(Wallet("", Coin("BTC", wallet: "0.02", positionIm: "0", orderIm: "0")));

        Assert.That(await Client().FreeAsync(Key, Secret, CancellationToken.None), Is.Zero);
    }

    // Spec exchange-accounts «Bybit не прислал число»: USDT is there, but what the free money is counted from is not; that is a
    // refusal, 502, never a nought.
    [TestCase("", "200", "50")]
    [TestCase("1500", "", "")]
    public void USDT_without_the_numbers_to_count_from_is_a_refusal_not_a_nought(string wallet, string positionIm, string orderIm)
    {
        _bybit.Json(Wallet("", Coin("USDT", wallet, positionIm, orderIm)));

        ExchangeException? refused = Assert.ThrowsAsync<ExchangeException>(() => Client().FreeAsync(Key, Secret, CancellationToken.None));

        Assert.That(refused, Is.Not.InstanceOf<ExchangeKeyRejectedException>().And.Not.InstanceOf<ExchangeUnavailableException>());
        Assert.That(refused!.Message, Is.EqualTo("Bybit не прислал свободные деньги аккаунта"));
    }

    // Spec exchange-accounts «Ключ больше не действует».
    [Test]
    public void The_balance_of_a_rejected_key_is_rejected_with_the_reason_of_Bybit()
    {
        _bybit.Json(Error(33004, "Your api key has expired."));

        ExchangeKeyRejectedException? rejected = Assert.ThrowsAsync<ExchangeKeyRejectedException>(() => Client().FreeAsync(Key, Secret, CancellationToken.None));

        Assert.That(rejected!.Message, Is.EqualTo("Your api key has expired."));
    }

    private BybitAccountClient Client(BybitSettings? settings = null)
    {
        return new BybitAccountClient(_bybit.Client(), settings ?? new BybitSettings(), new ListLogger<BybitAccountClient>(_log));
    }

    private static string KeyInfo(string permissions, int readOnly = 0, int uta = 1, long uid = 104857321)
    {
        return $$"""
                 {"retCode":0,"retMsg":"","result":{"id":"13770661","note":"bot","apiKey":"{{Key}}","readOnly":{{readOnly}},"secret":"","permissions":{{permissions}},
                 "ips":["*"],"type":1,"deadlineDay":66,"expiredAt":"2027-01-22T07:20:51Z","createdAt":"2026-11-16T02:24:40Z","unified":0,"uta":{{uta}},
                 "userID":{{uid}},"inviterID":0,"vipLevel":"No VIP","mktMakerLevel":"0","affiliateID":0,"rsaPublicKey":"","isMaster":true,"parentUid":"0",
                 "kycLevel":"LEVEL_DEFAULT","kycRegion":""},"retExtInfo":{},"time":1732788000000}
                 """;
    }

    private static string Balance(string available)
    {
        return $$"""
                 {"retCode":0,"retMsg":"OK","result":{"list":[{"totalEquity":"1890.12","accountType":"UNIFIED","totalWalletBalance":"1880.00",
                 "totalMarginBalance":"1890.12","totalAvailableBalance":"{{available}}","totalPerpUPL":"10.12","totalInitialMargin":"639.72",
                 "totalMaintenanceMargin":"31.5","accountIMRate":"0.33","accountMMRate":"0.01","accountLTV":"0","coin":[]}]},"retExtInfo":{},"time":1732788000000}
                 """;
    }

    // An answer of wallet-balance with the available balance of the whole account — absent when null — and the coins.
    private static string Wallet(string? available, params string[] coins)
    {
        string total = available is null ? "" : $"\"totalAvailableBalance\":\"{available}\",";
        return $$"""
                 {"retCode":0,"retMsg":"OK","result":{"list":[{"totalEquity":"","accountType":"UNIFIED","totalWalletBalance":"",
                 "totalMarginBalance":"",{{total}}"totalPerpUPL":"","totalInitialMargin":"","totalMaintenanceMargin":"",
                 "accountIMRate":"","accountMMRate":"","accountLTV":"","coin":[{{string.Join(",", coins)}}]}]},"retExtInfo":{},"time":1732788000000}
                 """;
    }

    private static string Coin(string coin, string wallet, string positionIm, string orderIm, string locked = "0", string bonus = "0")
    {
        return $$"""
                 {"coin":"{{coin}}","equity":"{{wallet}}","usdValue":"","walletBalance":"{{wallet}}","free":"","locked":"{{locked}}",
                 "borrowAmount":"0","availableToWithdraw":"","accruedInterest":"0","totalOrderIM":"{{orderIm}}","totalPositionIM":"{{positionIm}}",
                 "totalPositionMM":"","unrealisedPnl":"0","cumRealisedPnl":"0","bonus":"{{bonus}}","marginCollateral":true,"collateralSwitch":true}
                 """;
    }

    private static string Error(int code, string reason)
    {
        return $$"""{"retCode":{{code}},"retMsg":"{{reason}}","result":{},"retExtInfo":{},"time":1732788000000}""";
    }
}
