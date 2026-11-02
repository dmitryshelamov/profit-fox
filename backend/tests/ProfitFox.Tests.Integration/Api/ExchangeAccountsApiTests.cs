using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Dapper;

using Npgsql;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Serilog.Core;
using Serilog.Events;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class ExchangeAccountsApiTests
{
    private const string Accounts = "/api/exchange-accounts";
    private const string Secret = "secret-that-never-leaves";

    private static readonly ExchangeKeyInfo TradingKey = new ExchangeKeyInfo(
        100,
        IsReadOnly: false,
        IsUnifiedAccount: true,
        CanWithdraw: false,
        new HashSet<MarketCategory> { MarketCategory.Spot, MarketCategory.Linear });

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Workers = 0 };
        _factory.KeyInspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(TradingKey);
        _client = _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenarios "Валидный аккаунт", "Чтение сохранённого аккаунта" and "Чтение аккаунта без secret".
    [Test]
    public async Task Account_is_created_and_read_without_its_secret()
    {
        HttpResponseMessage created = await _client.PostAsJsonAsync(Accounts, Body());

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        created.Headers.Location!.ToString().ShouldEndWith($"{Accounts}/{id}");
        string read = await _client.GetStringAsync($"{Accounts}/{id}");
        string list = await _client.GetStringAsync(Accounts);
        JsonElement account = JsonDocument.Parse(read).RootElement;
        (account.GetProperty("name").GetString(), account.GetProperty("apiKey").GetString()).ShouldBe(("Основной", "api-key"));
        account.GetProperty("uid").GetInt64().ShouldBe(100);
        account.GetProperty("categories").EnumerateArray().Select(category => category.GetString()).ShouldBe(["spot", "linear"]);
        account.GetProperty("bots").GetArrayLength().ShouldBe(0);
        JsonDocument.Parse(list).RootElement.EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(id);
        read.ShouldNotContain(Secret);
        list.ShouldNotContain(Secret);
        await _factory.KeyInspector.Received(1).InspectAsync("api-key", Secret, Arg.Any<CancellationToken>());
    }

    // Scenario "Secret в базе зашифрован".
    [Test]
    public async Task Stored_row_holds_no_plain_secret()
    {
        await CreateAsync();

        await using var connection = new NpgsqlConnection(_factory.ConnectionString);
        string row = await connection.ExecuteScalarAsync<string>("SELECT row_to_json(a)::text FROM exchange_accounts a") ?? string.Empty;

        row.ShouldNotContain(Secret);
        row.ShouldNotContain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Secret)));
    }

    // Scenario "Пустое имя".
    [Test]
    public async Task Invalid_fields_are_a_bad_request()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(Accounts, Body(name: ""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("name", out _).ShouldBeTrue();
    }

    // Scenario "Bybit не принимает ключ"; the secret stays out of the answer and the log.
    [Test]
    public async Task Key_bybit_rejects_is_a_bad_request_on_the_key()
    {
        _factory.KeyInspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExchangeKeyRejectedException("Bybit does not accept the key: API key is invalid."));

        HttpResponseMessage response = await _client.PostAsJsonAsync(Accounts, Body());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string answer = await response.Content.ReadAsStringAsync();
        JsonDocument.Parse(answer).RootElement.GetProperty("errors").TryGetProperty("apiKey", out _).ShouldBeTrue();
        answer.ShouldNotContain(Secret);
        (await _client.GetFromJsonAsync<JsonElement>(Accounts)).GetArrayLength().ShouldBe(0);
    }

    // Scenario "Bybit не отвечает при создании".
    [Test]
    public async Task Bybit_unavailable_is_service_unavailable()
    {
        _factory.KeyInspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExchangeUnavailableException("Bybit is unavailable."));

        HttpResponseMessage response = await _client.PostAsJsonAsync(Accounts, Body());

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    // Bybit answered but refused for another reason, such as a clock out of its receive window: retrying will not help.
    [Test]
    public async Task Other_bybit_refusal_is_a_bad_gateway_with_its_reason()
    {
        _factory.KeyInspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExchangeException("10002: invalid request, please check your server timestamp or recv_window param"));

        HttpResponseMessage response = await _client.PostAsJsonAsync(Accounts, Body());

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()!.ShouldContain("server timestamp");
    }

    [Test]
    public async Task Other_bybit_refusal_of_the_balance_is_a_bad_gateway()
    {
        Guid id = await CreateAsync();
        _factory.KeyInspector.GetBalanceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExchangeException("10002: invalid request, please check your server timestamp or recv_window param"));

        (await _client.GetAsync($"{Accounts}/{id}/balance")).StatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    // Scenario "Неизвестный аккаунт".
    [Test]
    public async Task Unknown_account_is_not_found()
    {
        (await _client.GetAsync($"{Accounts}/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.GetAsync($"{Accounts}/{Guid.NewGuid()}/balance")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.PutAsJsonAsync($"{Accounts}/{Guid.NewGuid()}", new { name = "Other" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.DeleteAsync($"{Accounts}/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Scenario "Новое имя".
    [Test]
    public async Task Account_is_renamed()
    {
        Guid id = await CreateAsync();

        (await _client.PutAsJsonAsync($"{Accounts}/{id}", new { name = "Торговый" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _client.GetFromJsonAsync<JsonElement>($"{Accounts}/{id}")).GetProperty("name").GetString().ShouldBe("Торговый");
    }

    // Scenarios "Аккаунт без ботов" and "Аккаунт с ботом".
    [Test]
    public async Task Account_with_a_bot_is_kept_and_without_bots_is_deleted()
    {
        Guid id = await CreateAsync();
        Guid botId = await CreateBotAsync(id);

        HttpResponseMessage conflict = await _client.DeleteAsync($"{Accounts}/{id}");

        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("botNames").EnumerateArray().Select(name => name.GetString()).ShouldBe(["BTC EMA час"]);
        (await _client.GetFromJsonAsync<JsonElement>($"{Accounts}/{id}")).GetProperty("bots").EnumerateArray().Single().GetProperty("id").GetGuid().ShouldBe(botId);

        (await _client.DeleteAsync($"/api/bots/{botId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.DeleteAsync($"{Accounts}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.GetAsync($"{Accounts}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Scenario "Баланс аккаунта".
    [Test]
    public async Task Balance_is_read_from_bybit_with_the_stored_key()
    {
        Guid id = await CreateAsync();
        _factory.KeyInspector.GetBalanceAsync("api-key", Secret, Arg.Any<CancellationToken>())
            .Returns(new AccountBalance(1675.31m, [new CoinBalance("USDT", 1000.123456m, 1000.25m), new CoinBalance("BTC", 0.01m, 675.06m)]));

        JsonElement balance = await _client.GetFromJsonAsync<JsonElement>($"{Accounts}/{id}/balance");

        JsonNode.DeepEquals(
                JsonNode.Parse(balance.GetRawText()),
                JsonNode.Parse(
                    """
                    {"totalEquityUsd":"1675.31","coins":[{"coin":"USDT","walletBalance":"1000.123456","usdValue":"1000.25"},{"coin":"BTC","walletBalance":"0.01","usdValue":"675.06"}]}
                    """))
            .ShouldBeTrue(balance.GetRawText());
    }

    // Scenario "Ключ отозван".
    [Test]
    public async Task Balance_of_a_key_bybit_no_longer_accepts_is_a_conflict()
    {
        Guid id = await CreateAsync();
        _factory.KeyInspector.GetBalanceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExchangeKeyRejectedException("Bybit does not accept the key: API key is invalid."));

        (await _client.GetAsync($"{Accounts}/{id}/balance")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Balance_while_bybit_is_unavailable_is_service_unavailable()
    {
        Guid id = await CreateAsync();
        _factory.KeyInspector.GetBalanceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExchangeUnavailableException("Bybit is unavailable."));

        (await _client.GetAsync($"{Accounts}/{id}/balance")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    // Requirement "Secret не выдаётся": with every logger at Verbose and the app's own Bybit client and HTTP pipeline, no event of
    // an accepted or a rejected key, nor of the balance, carries the secret.
    [Test]
    public async Task Secret_never_reaches_the_log()
    {
        var logs = new List<LogEvent>();
        ILogEventSink sink = Substitute.For<ILogEventSink>();
        sink.When(log => log.Emit(Arg.Any<LogEvent>())).Do(call =>
        {
            lock (logs)
            {
                logs.Add(call.Arg<LogEvent>()!);
            }
        });
        var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Workers = 0, LogSink = sink, FakeKeyInspector = false, VerboseLogs = true };

        await using (factory)
        {
            using HttpClient client = factory.CreateClient();
            HttpResponseMessage created = await client.PostAsJsonAsync(Accounts, Body());
            created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            Guid id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            (await client.GetAsync($"{Accounts}/{id}/balance")).StatusCode.ShouldBe(HttpStatusCode.OK);
            factory.Bybit.RejectKeys = true;
            (await client.PostAsJsonAsync(Accounts, Body())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await client.GetAsync($"{Accounts}/{id}/balance")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        lock (logs)
        {
            logs.ShouldContain(log => log.Level == LogEventLevel.Verbose || log.Level == LogEventLevel.Debug);
            logs.ShouldContain(log => log.RenderMessage(null).Contains("/v5/user/query-api"));
            logs.ShouldAllBe(log => !Rendered(log).Contains(Secret));
        }
    }

    private static string Rendered(LogEvent log)
    {
        return string.Join(" ", log.RenderMessage(null), log.Exception, string.Join(" ", log.Properties.Values));
    }

    private async Task<Guid> CreateAsync()
    {
        HttpResponseMessage created = await _client.PostAsJsonAsync(Accounts, Body());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateBotAsync(Guid accountId)
    {
        _factory.Bybit.Instruments.Add(("spot", "BTCUSDT"));
        HttpResponseMessage strategy = await _client.PostAsJsonAsync("/api/strategies", new JsonObject { ["name"] = "EMA", ["definition"] = JsonNode.Parse(StrategyFixtures.Element("ema").GetRawText()) });
        Guid strategyId = (await strategy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        HttpResponseMessage bot = await _client.PostAsJsonAsync(
            "/api/bots",
            new JsonObject
            {
                ["name"] = "BTC EMA час",
                ["strategyId"] = strategyId,
                ["category"] = "spot",
                ["symbol"] = "BTCUSDT",
                ["interval"] = "60",
                ["balance"] = 1000,
                ["exchangeAccountId"] = accountId
            });
        bot.StatusCode.ShouldBe(HttpStatusCode.Created, await bot.Content.ReadAsStringAsync());

        return (await bot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static JsonObject Body(string name = "Основной")
    {
        return new JsonObject { ["name"] = name, ["apiKey"] = "api-key", ["apiSecret"] = Secret };
    }
}