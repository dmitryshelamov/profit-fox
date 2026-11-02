using System.Net;

using Microsoft.Extensions.Logging.Abstractions;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Infrastructure.Exchanges.Bybit;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

public class BybitAccountClientTests
{
    private const string Secret = "secret-value-never-shown";

    private const string FullPermissions =
        """{"ContractTrade":["Order","Position"],"Spot":["SpotTrade"],"Wallet":["AccountTransfer","SubMemberTransfer"],"Options":[],"Derivatives":[],"CopyTrading":[],"BlockTrade":[],"Exchange":[],"NFT":[],"Affiliate":[]}""";

    [Test]
    public async Task Trading_key_of_a_unified_account()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(KeyInfo(FullPermissions));

        ExchangeKeyInfo key = await InspectAsync(handler);

        key.ShouldBeEquivalentTo(new ExchangeKeyInfo(24617703, false, true, false, new HashSet<MarketCategory> { MarketCategory.Spot, MarketCategory.Linear }));
        handler.Requests.Single().Host.ShouldBe("api.bybit.com");
        handler.Requests.Single().AbsolutePath.ShouldBe("/v5/user/query-api");
    }

    // The end-to-end stub: a configured address takes the calls instead of the main network.
    [Test]
    public async Task Configured_address_takes_the_calls()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(KeyInfo(FullPermissions));

        await Client(handler, new BybitSettings("http://127.0.0.1:5296")).InspectAsync("key", Secret, CancellationToken.None);

        handler.Requests.Single().Authority.ShouldBe("127.0.0.1:5296");
    }

    [Test]
    public async Task Spot_only_key_trades_only_spot()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(KeyInfo("""{"ContractTrade":[],"Spot":["SpotTrade"],"Wallet":[]}"""));

        ExchangeKeyInfo key = await InspectAsync(handler);

        key.TradableCategories.ShouldBe([MarketCategory.Spot]);
    }

    // Placing orders without managing positions is not enough for a linear bot.
    [Test]
    public async Task Linear_needs_both_orders_and_positions()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(KeyInfo("""{"ContractTrade":["Order"],"Spot":[],"Wallet":[]}"""));

        ExchangeKeyInfo key = await InspectAsync(handler);

        key.TradableCategories.ShouldBeEmpty();
    }

    [Test]
    public async Task Withdrawal_permission_is_reported()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(KeyInfo("""{"ContractTrade":[],"Spot":["SpotTrade"],"Wallet":["AccountTransfer","Withdraw"]}"""));

        ExchangeKeyInfo key = await InspectAsync(handler);

        key.CanWithdraw.ShouldBeTrue();
    }

    [Test]
    public async Task Read_only_key_of_a_classic_account_is_reported()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(KeyInfo(FullPermissions, readOnly: 1, uta: 0));

        ExchangeKeyInfo key = await InspectAsync(handler);

        key.IsReadOnly.ShouldBeTrue();
        key.IsUnifiedAccount.ShouldBeFalse();
    }

    // Scenario "Bybit не принимает ключ": invalid key, signature, timestamp-free errors of the key itself.
    [TestCase(10003)]
    [TestCase(10004)]
    [TestCase(10005)]
    [TestCase(10010)]
    [TestCase(33004)]
    public async Task Key_bybit_does_not_accept_is_rejected(int retCode)
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(BybitJson.Error(retCode, "API key is invalid."));

        ExchangeKeyRejectedException exception = await Should.ThrowAsync<ExchangeKeyRejectedException>(() => InspectAsync(handler));

        exception.Message.ShouldNotContain(Secret);
    }

    // Scenario "Bybit не отвечает при создании": one attempt, as the user waits for the answer.
    [Test]
    public async Task Server_error_means_bybit_is_unavailable()
    {
        using StubHttpHandler handler = new StubHttpHandler().Respond(HttpStatusCode.ServiceUnavailable);

        await Should.ThrowAsync<ExchangeUnavailableException>(() => InspectAsync(handler));
        handler.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task Connection_failure_means_bybit_is_unavailable()
    {
        using StubHttpHandler handler = new StubHttpHandler().Respond(() => throw new HttpRequestException("connection refused"));

        await Should.ThrowAsync<ExchangeUnavailableException>(() => InspectAsync(handler));
    }

    [Test]
    public async Task Rate_limit_means_bybit_is_unavailable()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(BybitJson.Error(10006, "Too many visits!"));

        await Should.ThrowAsync<ExchangeUnavailableException>(() => InspectAsync(handler));
    }

    [Test]
    public async Task Other_bybit_error_is_an_exchange_error()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(BybitJson.Error(10001, "Params error."));

        ExchangeException exception = await Should.ThrowAsync<ExchangeException>(() => InspectAsync(handler));

        exception.ShouldNotBeOfType<ExchangeKeyRejectedException>();
        exception.ShouldNotBeOfType<ExchangeUnavailableException>();
    }

    // Scenario "Баланс аккаунта".
    [Test]
    public async Task Balance_lists_coins_held_most_valuable_first()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(
            Balance(
                "1675.31",
                Coin("USDT", "1000.123456", "1000.25"),
                Coin("ETH", "0", "0"),
                Coin("BTC", "0.01", "675.06")));

        AccountBalance balance = await Client(handler).GetBalanceAsync("key", Secret, CancellationToken.None);

        balance.TotalEquityUsd.ShouldBe(1675.31m);
        balance.Coins.ShouldBe([new CoinBalance("USDT", 1000.123456m, 1000.25m), new CoinBalance("BTC", 0.01m, 675.06m)]);
        BybitRequests.Describe(handler.Requests.Single()).ShouldBe(BybitRequests.Describe("/v5/account/wallet-balance?accountType=UNIFIED"));
        handler.Requests.Single().Host.ShouldBe("api.bybit.com");
    }

    // Bybit writes an empty string for a value it has not computed: a coin without a balance is left out, an unknown value is 0.
    [Test]
    public async Task Empty_values_of_bybit_are_no_balance()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(Balance("", Coin("USDT", "1000", ""), Coin("ETH", "", "")));

        AccountBalance balance = await Client(handler).GetBalanceAsync("key", Secret, CancellationToken.None);

        balance.TotalEquityUsd.ShouldBe(0m);
        balance.Coins.ShouldBe([new CoinBalance("USDT", 1000m, 0m)]);
    }

    [Test]
    public async Task Account_bybit_lists_no_wallet_for_has_an_empty_balance()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson("""{"retCode":0,"retMsg":"OK","result":{"list":[]},"retExtInfo":{},"time":0}""");

        AccountBalance balance = await Client(handler).GetBalanceAsync("key", Secret, CancellationToken.None);

        balance.TotalEquityUsd.ShouldBe(0m);
        balance.Coins.ShouldBeEmpty();
    }

    // Scenario "Ключ отозван".
    [Test]
    public async Task Balance_of_a_rejected_key_is_rejected()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(BybitJson.Error(10003, "API key is invalid."));

        await Should.ThrowAsync<ExchangeKeyRejectedException>(() => Client(handler).GetBalanceAsync("key", Secret, CancellationToken.None));
    }

    [Test]
    public async Task Balance_server_error_means_bybit_is_unavailable()
    {
        using StubHttpHandler handler = new StubHttpHandler().Respond(HttpStatusCode.BadGateway);

        await Should.ThrowAsync<ExchangeUnavailableException>(() => Client(handler).GetBalanceAsync("key", Secret, CancellationToken.None));
    }

    private static Task<ExchangeKeyInfo> InspectAsync(StubHttpHandler handler)
    {
        return Client(handler).InspectAsync("key", Secret, CancellationToken.None);
    }

    private static BybitAccountClient Client(StubHttpHandler handler, BybitSettings? settings = null)
    {
        return new BybitAccountClient(handler.CreateClient(), NullLoggerFactory.Instance, settings ?? new BybitSettings());
    }

    private static string KeyInfo(string permissions, int readOnly = 0, int uta = 1)
    {
        return $$"""
                 {"retCode":0,"retMsg":"","result":{"id":"13770661","note":"bot","apiKey":"key","readOnly":{{readOnly}},"secret":"","permissions":{{permissions}},
                 "ips":["*"],"type":1,"deadlineDay":66,"expiredAt":"2023-12-22T07:20:51Z","createdAt":"2023-10-16T02:24:40Z","unified":0,"uta":{{uta}},
                 "userID":24617703,"inviterID":0,"vipLevel":"No VIP","mktMakerLevel":"0","affiliateID":0,"rsaPublicKey":"","isMaster":true,"parentUid":"0",
                 "kycLevel":"LEVEL_DEFAULT","kycRegion":""},"retExtInfo":{},"time":1697525990798}
                 """;
    }

    private static string Balance(string totalEquity, params string[] coins)
    {
        return $$"""
                 {"retCode":0,"retMsg":"OK","result":{"list":[{"totalEquity":"{{totalEquity}}","accountType":"UNIFIED","totalWalletBalance":"0",
                 "totalMarginBalance":"0","totalAvailableBalance":"0","totalPerpUPL":"0","totalInitialMargin":"0","totalMaintenanceMargin":"0",
                 "accountIMRate":"0","accountMMRate":"0","accountLTV":"0","coin":[{{string.Join(",", coins)}}]}]},"retExtInfo":{},"time":1690872862481}
                 """;
    }

    private static string Coin(string coin, string walletBalance, string usdValue)
    {
        return $$"""{"coin":"{{coin}}","walletBalance":"{{walletBalance}}","usdValue":"{{usdValue}}","equity":"{{walletBalance}}","locked":"0","borrowAmount":"0"}""";
    }
}