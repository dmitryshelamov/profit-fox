using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class BotsApiTests
{
    private const string Bots = "/api/bots";

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _ema;
    private Guid _emaShort;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync();
        _factory.Bybit.Instruments.UnionWith([("spot", "BTCUSDT"), ("spot", "ETHUSDT"), ("linear", "BTCUSDT")]);
        _client = _factory.CreateClient();
        _ema = await CreateStrategyAsync("EMA", StrategyFixtures.Element("ema"));
        _emaShort = await CreateStrategyAsync("EMA Short", StrategyFixtures.Element("ema-short"));
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenarios "Валидный бот" and "Чтение сохранённого бота".
    [Test]
    public async Task Valid_bot_is_created_and_read_back()
    {
        HttpResponseMessage created = await _client.PostAsJsonAsync(Bots, Body());

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        created.Headers.Location!.ToString().ShouldEndWith($"{Bots}/{id}");
        JsonElement bot = await _client.GetFromJsonAsync<JsonElement>($"{Bots}/{id}");
        bot.GetProperty("name").GetString().ShouldBe("EMA BTC");
        bot.GetProperty("strategyId").GetGuid().ShouldBe(_ema);
        JsonNode.DeepEquals(JsonNode.Parse(bot.GetProperty("parameterValues").GetRawText()), JsonNode.Parse("""{ "fast": 10 }""")).ShouldBeTrue();
        (bot.GetProperty("category").GetString(), bot.GetProperty("symbol").GetString(), bot.GetProperty("interval").GetString()).ShouldBe(("spot", "BTCUSDT", "60"));
        bot.GetProperty("balance").GetString().ShouldBe("1000.5");
    }

    [Test]
    public async Task List_gives_id_name_strategy_instrument_and_interval()
    {
        Guid id = await CreateBotAsync(Body());

        JsonElement item = (await _client.GetFromJsonAsync<JsonElement>(Bots)).EnumerateArray().ShouldHaveSingleItem();

        item.GetProperty("id").GetGuid().ShouldBe(id);
        (item.GetProperty("name").GetString(), item.GetProperty("strategyId").GetGuid()).ShouldBe(("EMA BTC", _ema));
        (item.GetProperty("category").GetString(), item.GetProperty("symbol").GetString(), item.GetProperty("interval").GetString()).ShouldBe(("spot", "BTCUSDT", "60"));
    }

    // Scenarios "Пустое имя", "Значение вне ограничений", "Шорт на споте", "Неизвестная стратегия", "Нулевой баланс".
    [TestCase("name", "", "name")]
    [TestCase("fast", 1, "parameterValues.fast")]
    [TestCase("fast", "x", "parameterValues.fast")]
    [TestCase("short", null, "category")]
    [TestCase("strategyId", null, "strategyId")]
    [TestCase("balance", 0, "balance")]
    [TestCase("interval", "7", "interval")]
    [TestCase("category", "inverse", "category")]
    [TestCase("parameterValues", 5, "parameterValues")]
    [TestCase("no strategy", null, "strategyId")]
    public async Task Invalid_bot_is_rejected_with_an_error_by_field(string field, object? value, string error)
    {
        JsonObject body = Body();
        switch (field)
        {
            case "fast":
                body["parameterValues"]!["fast"] = JsonValue.Create(value);

                break;
            case "short":
                body["strategyId"] = _emaShort;

                break;
            case "strategyId":
                body["strategyId"] = Guid.NewGuid();

                break;
            case "no strategy":
                body.Remove("strategyId");

                break;
            default:
                body[field] = JsonValue.Create(value);

                break;
        }

        HttpResponseMessage response = await _client.PostAsJsonAsync(Bots, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateObject().Select(item => item.Name).ShouldBe([error]);
        (await _client.GetFromJsonAsync<JsonElement>(Bots)).GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Bot_without_parameter_values_takes_every_default()
    {
        JsonObject body = Body();
        body.Remove("parameterValues");

        Guid id = await CreateBotAsync(body);

        (await _client.GetFromJsonAsync<JsonElement>($"{Bots}/{id}")).GetProperty("parameterValues").EnumerateObject().ShouldBeEmpty();
    }

    // Scenario "Символа нет на бирже".
    [Test]
    public async Task Bot_with_a_symbol_bybit_does_not_know_is_rejected()
    {
        JsonObject body = Body();
        body["symbol"] = "NOPEUSDT";

        HttpResponseMessage response = await _client.PostAsJsonAsync(Bots, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("symbol", out _).ShouldBeTrue();
    }

    [Test]
    public async Task Bot_is_not_created_while_bybit_cannot_confirm_the_symbol()
    {
        _factory.Bybit.Unavailable = true;

        HttpResponseMessage response = await _client.PostAsJsonAsync(Bots, Body());

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await _client.GetFromJsonAsync<JsonElement>(Bots)).GetArrayLength().ShouldBe(0);
    }

    // Scenario "Смена инструмента".
    [Test]
    public async Task Bot_is_changed()
    {
        Guid id = await CreateBotAsync(Body());
        JsonObject body = Body();
        body["symbol"] = "ETHUSDT";
        body.Remove("strategyId");

        (await _client.PutAsJsonAsync($"{Bots}/{id}", body)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _client.GetFromJsonAsync<JsonElement>($"{Bots}/{id}")).GetProperty("symbol").GetString().ShouldBe("ETHUSDT");
    }

    // Scenario "Изменение с ошибкой".
    [Test]
    public async Task Invalid_change_keeps_the_bot()
    {
        Guid id = await CreateBotAsync(Body());
        JsonObject body = Body();
        body["parameterValues"]!["fast"] = 1;

        HttpResponseMessage response = await _client.PutAsJsonAsync($"{Bots}/{id}", body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("parameterValues.fast", out _).ShouldBeTrue();
        (await _client.GetFromJsonAsync<JsonElement>($"{Bots}/{id}")).GetProperty("parameterValues").GetProperty("fast").GetDecimal().ShouldBe(10m);
    }

    // Scenario "Неизвестный бот".
    [Test]
    public async Task Unknown_bot_is_not_found()
    {
        var id = Guid.NewGuid();

        (await _client.GetAsync($"{Bots}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.PutAsJsonAsync($"{Bots}/{id}", Body())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.DeleteAsync($"{Bots}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Bot_is_deleted()
    {
        Guid id = await CreateBotAsync(Body());

        (await _client.DeleteAsync($"{Bots}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _client.GetAsync($"{Bots}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Scenario "Удалён параметр со значением у бота".
    [Test]
    public async Task Strategy_change_that_breaks_a_bot_is_a_conflict_listing_the_bot()
    {
        Guid id = await CreateBotAsync(Body());
        JsonNode definition = JsonNode.Parse(StrategyFixtures.Json("ema"))!;
        definition["parameters"]!.AsArray().RemoveAt(0);
        definition["wires"]!.AsArray().RemoveAt(1);
        definition["nodes"]![3]!["inputs"] = new JsonObject { ["fast"] = 5 };

        HttpResponseMessage response = await _client.PutAsJsonAsync($"/api/strategies/{_ema}", new { name = "EMA", definition });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonElement bot = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("bots").EnumerateArray().ShouldHaveSingleItem();
        (bot.GetProperty("id").GetGuid(), bot.GetProperty("name").GetString()).ShouldBe((id, "EMA BTC"));
        bot.GetProperty("errors").GetProperty("parameterValues.fast").GetString().ShouldBe("No such parameter.");
        (await _client.GetFromJsonAsync<JsonElement>($"/api/strategies/{_ema}")).GetProperty("version").GetInt32().ShouldBe(1);
    }

    // Scenario "Удаление стратегии с ботами".
    [Test]
    public async Task Strategy_with_a_bot_is_not_deleted()
    {
        Guid id = await CreateBotAsync(Body());

        HttpResponseMessage response = await _client.DeleteAsync($"/api/strategies/{_ema}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("bots").EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(id);
        (await _client.GetAsync($"/api/strategies/{_ema}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private JsonObject Body()
    {
        return new JsonObject
        {
            ["name"] = "EMA BTC",
            ["strategyId"] = _ema,
            ["parameterValues"] = new JsonObject { ["fast"] = 10 },
            ["category"] = "spot",
            ["symbol"] = "BTCUSDT",
            ["interval"] = "60",
            ["balance"] = 1000.5m
        };
    }

    private async Task<Guid> CreateBotAsync(JsonObject body)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(Bots, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateStrategyAsync(string name, JsonElement definition)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/strategies", new { name, definition });

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}