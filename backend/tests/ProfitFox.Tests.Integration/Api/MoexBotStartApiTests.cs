using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using NSubstitute;

using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Infrastructure.Exchanges.TInvest;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>
///     Requirements "Торговля на MOEX выключателем" and "Запуск бота" on MOEX: the start of a bot on <c>moex</c> / <c>SBER</c> with a T-Invest
///     account through the API, with the trading on MOEX off (the default) and on (<c>TInvest:Trading=true</c>).
/// </summary>
public class MoexBotStartApiTests
{
    private const string Token = "t.token";
    private const string AccountNumber = "2000000001";

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Торговля на MOEX выключена": no request goes to T-Invest, and the bot stays stopped.
    [Test]
    public async Task Bot_on_moex_does_not_start_while_the_trading_on_moex_is_off()
    {
        Guid bot = await CreateBotAsync(trading: false);
        int calls = _factory.TInvest.Calls.Count;
        _factory.BrokerAccounts.ClearReceivedCalls();

        HttpResponseMessage response = await _client.PostAsync($"/api/bots/{bot}/start", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString().ShouldBe("Trading on MOEX is disabled.");
        _factory.TInvest.Calls.Count.ShouldBe(calls);
        _factory.BrokerAccounts.ReceivedCalls().ShouldBeEmpty();
        (await StatusAsync(bot)).ShouldBe("stopped");
    }

    // Scenarios "Торговля на MOEX включена" and "Бот на MOEX".
    [Test]
    public async Task Bot_on_moex_starts_while_the_trading_on_moex_is_on()
    {
        Guid bot = await CreateBotAsync(trading: true);

        HttpResponseMessage response = await _client.PostAsync($"/api/bots/{bot}/start", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await StatusAsync(bot)).ShouldBe("running");
        await _factory.Ledger.Received().GetFreeAsync(new ExchangeKey(AccountNumber, Token, Exchange.TInvest), "RUB", Arg.Any<CancellationToken>());
    }

    private async Task<Guid> CreateBotAsync(bool trading)
    {
        _factory = new ProfitFoxFactory(IntegrationEnvironment.ConnectionString)
        {
            Workers = 0,
            ApiSettings = new Dictionary<string, string?> { ["TInvest:Trading"] = trading ? "true" : "false" }
        };
        _factory.BrokerAccounts.ListAccountsAsync(Token, Arg.Any<CancellationToken>())
            .Returns([new BrokerAccount(AccountNumber, "Брокерский счёт", BrokerAccountType.Brokerage, BrokerAccountStatus.Open, BrokerAccountAccess.Full)]);
        _factory.Ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), "RUB", Arg.Any<CancellationToken>()).Returns(100_000m);
        _factory.TInvest
            .On<InstrumentRequest, ShareResponse>("ShareBy", _ => new ShareResponse { Instrument = Sber() })
            .On<InstrumentsRequest, SharesResponse>("Shares", _ => new SharesResponse { Instruments = { Sber() } });
        _client = _factory.CreateClient();
        HttpResponseMessage account = await _client.PostAsJsonAsync("/api/exchange-accounts", new { name = "Брокерский", exchange = "tinvest", token = Token, accountId = AccountNumber });
        account.StatusCode.ShouldBe(HttpStatusCode.Created, await account.Content.ReadAsStringAsync());
        Guid accountId = (await account.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        HttpResponseMessage strategy = await _client.PostAsJsonAsync("/api/strategies", new { name = "EMA", definition = StrategyFixtures.Element("ema") });
        Guid strategyId = (await strategy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        HttpResponseMessage bot = await _client.PostAsJsonAsync(
            "/api/bots",
            new JsonObject
            {
                ["name"] = "SBER EMA",
                ["strategyId"] = strategyId,
                ["category"] = "moex",
                ["symbol"] = "SBER",
                ["interval"] = "60",
                ["balance"] = 50000,
                ["exchangeAccountId"] = accountId
            });
        bot.StatusCode.ShouldBe(HttpStatusCode.Created, await bot.Content.ReadAsStringAsync());

        return (await bot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<string?> StatusAsync(Guid bot)
    {
        return (await _client.GetFromJsonAsync<JsonElement>($"/api/bots/{bot}")).GetProperty("status").GetString();
    }

    private static Share Sber()
    {
        return new Share
        {
            Uid = "e6123145-9665-43e0-8413-cd61b8aa9b13",
            Ticker = "SBER",
            ClassCode = "TQBR",
            Name = "Сбер Банк",
            Currency = "rub",
            Lot = 10,
            MinPriceIncrement = 0.01m.ToQuotation(),
            ApiTradeAvailableFlag = true
        };
    }
}