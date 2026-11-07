using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using NSubstitute;

using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class BotAccountApiTests
{
    private const string Bots = "/api/bots";

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _ema;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new ProfitFoxFactory(IntegrationEnvironment.ConnectionString) { Workers = 0 };
        _factory.Bybit.Instruments.UnionWith([("spot", "BTCUSDT"), ("linear", "BTCUSDT")]);
        _client = _factory.CreateClient();
        HttpResponseMessage strategy = await _client.PostAsJsonAsync("/api/strategies", new { name = "EMA", definition = StrategyFixtures.Element("ema") });
        _ema = (await strategy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenarios "Бот с аккаунтом" and "Аккаунт в списке".
    [Test]
    public async Task Bot_and_the_list_of_bots_show_the_account()
    {
        Guid account = await CreateAccountAsync("Проверка", MarketCategory.Spot);

        Guid id = await CreateBotAsync(Body(account));

        JsonElement read = (await _client.GetFromJsonAsync<JsonElement>($"{Bots}/{id}")).GetProperty("exchangeAccount");
        JsonElement listed = (await _client.GetFromJsonAsync<JsonElement>(Bots)).EnumerateArray().Single().GetProperty("exchangeAccount");
        foreach (JsonElement shown in new[] { read, listed })
        {
            shown.GetProperty("id").GetGuid().ShouldBe(account);
            shown.GetProperty("name").GetString().ShouldBe("Проверка");
        }
    }

    // Scenarios "Бот без аккаунта" and "Снять аккаунт".
    [Test]
    public async Task Account_is_optional_and_can_be_removed()
    {
        Guid account = await CreateAccountAsync("Проверка", MarketCategory.Spot);
        Guid id = await CreateBotAsync(Body(account));

        JsonObject body = Body(null);
        (await _client.PutAsJsonAsync($"{Bots}/{id}", body)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _client.GetFromJsonAsync<JsonElement>($"{Bots}/{id}")).GetProperty("exchangeAccount").ValueKind.ShouldBe(JsonValueKind.Null);
        (await _client.GetFromJsonAsync<JsonElement>(Bots)).EnumerateArray().Single().GetProperty("exchangeAccount").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // Scenario "Неизвестный аккаунт".
    [Test]
    public async Task Unknown_account_is_an_error_of_the_account()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(Bots, Body(Guid.NewGuid()));

        await ShouldBeAccountErrorAsync(response);
    }

    // Scenarios "Ключ не торгует в категории бота" and "Смена категории при аккаунте только для спота".
    [Test]
    public async Task Account_that_does_not_trade_the_category_is_an_error_of_the_account()
    {
        Guid account = await CreateAccountAsync("Спот", MarketCategory.Spot);
        JsonObject linear = Body(account);
        linear["category"] = "linear";

        await ShouldBeAccountErrorAsync(await _client.PostAsJsonAsync(Bots, linear));

        Guid id = await CreateBotAsync(Body(account));
        await ShouldBeAccountErrorAsync(await _client.PutAsJsonAsync($"{Bots}/{id}", linear));
        (await _client.GetFromJsonAsync<JsonElement>($"{Bots}/{id}")).GetProperty("category").GetString().ShouldBe("spot");
    }

    private static async Task ShouldBeAccountErrorAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("exchangeAccountId", out _).ShouldBeTrue();
    }

    private async Task<Guid> CreateAccountAsync(string name, params MarketCategory[] categories)
    {
        _factory.KeyInspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ExchangeKeyInfo(Random.Shared.NextInt64(1, long.MaxValue), false, true, false, categories.ToHashSet()));
        HttpResponseMessage created = await _client.PostAsJsonAsync(
            "/api/exchange-accounts",
            new { name, apiKey = "api-key", apiSecret = "api-secret" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateBotAsync(JsonObject body)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(Bots, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private JsonObject Body(Guid? account)
    {
        return new JsonObject
        {
            ["name"] = "BTC EMA час",
            ["strategyId"] = _ema,
            ["category"] = "spot",
            ["symbol"] = "BTCUSDT",
            ["interval"] = "60",
            ["balance"] = 1000,
            ["exchangeAccountId"] = account
        };
    }
}