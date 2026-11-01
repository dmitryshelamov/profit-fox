using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class BacktestsApiTests
{
    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private BacktestScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync();
        _factory.Bybit.Rules[("linear", "BTCUSDT")] = ("0.10", "0.001", "0.001", "5");
        _client = _factory.CreateClient();
        _scenario = new BacktestScenario(_factory, _client);
        await _scenario.CreateBotAsync("ema", new { fast = 10 });
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenarios "Бэктест создан", "Значения по умолчанию" and "Правила инструмента".
    [Test]
    public async Task Backtest_is_created_pending_at_the_latest_version_with_the_rules_of_the_instrument()
    {
        HttpResponseMessage response = await CreateAsync(Body());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().ShouldEndWith($"/api/backtests/{id}");
        JsonElement backtest = await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{id}");
        (backtest.GetProperty("botId").GetGuid(), backtest.GetProperty("strategyId").GetGuid()).ShouldBe((_scenario.BotId, _scenario.StrategyId));
        backtest.GetProperty("strategyVersion").GetInt32().ShouldBe(1);
        backtest.GetProperty("engineVersion").GetInt32().ShouldBe(EngineVersion.Current);
        (backtest.GetProperty("category").GetString(), backtest.GetProperty("symbol").GetString(), backtest.GetProperty("interval").GetString()).ShouldBe(("linear", "BTCUSDT", "60"));
        (backtest.GetProperty("from").GetDateTimeOffset(), backtest.GetProperty("to").GetDateTimeOffset())
            .ShouldBe((DateTimeOffset.Parse("2024-01-01T00:00:00Z"), DateTimeOffset.Parse("2024-02-01T00:00:00Z")));
        backtest.GetProperty("status").GetString().ShouldBeOneOf("Pending", "LoadingCandles", "Running", "Succeeded");
        JsonNode.DeepEquals(
                JsonNode.Parse(backtest.GetProperty("parameters").GetRawText()),
                JsonNode.Parse("""{ "fast": { "min": 5, "max": 15, "step": 5 }, "slow": 26, "amount": 100 }"""))
            .ShouldBeTrue();
        backtest.GetProperty("searchMode").GetString().ShouldBe("grid");
        JsonNode.DeepEquals(
                JsonNode.Parse(backtest.GetProperty("simulation").GetRawText()),
                JsonNode.Parse("""{ "balance": "1000", "limitFeePercent": "0.02", "marketFeePercent": "0.055", "slippagePercent": "0", "limitFillMode": "breakout" }"""))
            .ShouldBeTrue();
        JsonNode.DeepEquals(
                JsonNode.Parse(backtest.GetProperty("rules").GetRawText()),
                JsonNode.Parse("""{ "tickSize": "0.10", "quantityStep": "0.001", "minQuantity": "0.001", "minAmount": "5" }"""))
            .ShouldBeTrue();
        backtest.GetProperty("progress").GetProperty("totalCombinations").GetInt64().ShouldBe(3);
    }

    [Test]
    public async Task Simulation_settings_are_taken_as_given()
    {
        JsonObject body = Body();
        body["simulation"] = new JsonObject
        {
            ["balance"] = 500,
            ["limitFeePercent"] = 0,
            ["marketFeePercent"] = 0.1,
            ["slippagePercent"] = 0.05,
            ["limitFillMode"] = "touch"
        };
        body["searchMode"] = "grid";

        Guid id = (await (await CreateAsync(body)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        JsonElement simulation = (await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{id}")).GetProperty("simulation");
        (simulation.GetProperty("balance").GetString(), simulation.GetProperty("limitFillMode").GetString()).ShouldBe(("500", "touch"));
    }

    [TestCase("from", "2024-01-01", "from")]
    [TestCase("to", "2030-01-01T00:00:00Z", "to")]
    [TestCase("from", "2024-03-01T00:00:00Z", "period")]
    [TestCase("parameters", """{ "fast": { "min": 5, "max": 12, "step": 5 } }""", "parameters.fast")]
    [TestCase("parameters", """{ "fast": "x" }""", "parameters.fast")]
    [TestCase("parameters", """{ "fast": { "min": 5, "max": 15 } }""", "parameters.fast")]
    [TestCase("to", "tomorrow", "to")]
    [TestCase("parameters", """{ "fast": { "min": 2, "max": 200, "step": 1 }, "slow": { "min": 2, "max": 400, "step": 1 } }""", "parameters")]
    [TestCase("parameters", "7", "parameters")]
    [TestCase("searchMode", "random", "searchMode")]
    [TestCase("simulation", """{ "limitFillMode": "maybe" }""", "simulation.limitFillMode")]
    [TestCase("simulation", """{ "balance": 0 }""", "simulation.balance")]
    [TestCase("simulation", """{ "limitFeePercent": -1 }""", "simulation.limitFeePercent")]
    [TestCase("simulation", """{ "marketFeePercent": -1 }""", "simulation.marketFeePercent")]
    [TestCase("simulation", """{ "slippagePercent": 100 }""", "simulation.slippagePercent")]
    public async Task Invalid_backtest_is_rejected_with_an_error_by_field(string field, string value, string error)
    {
        JsonObject body = Body();
        body[field] = field is "parameters" or "simulation" ? JsonNode.Parse(value) : value;

        HttpResponseMessage response = await CreateAsync(body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateObject().Select(item => item.Name).ShouldBe([error]);
    }

    [Test]
    public async Task Backtest_of_an_unknown_bot_is_not_found()
    {
        (await _client.PostAsJsonAsync($"/api/bots/{Guid.NewGuid()}/backtests", Body())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/bots/{Guid.NewGuid()}/backtests")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/backtests/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.PostAsync($"/api/backtests/{Guid.NewGuid()}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Scenario "Биржа недоступна".
    [Test]
    public async Task Backtest_is_not_created_while_bybit_does_not_give_the_rules()
    {
        _factory.Bybit.Unavailable = true;

        HttpResponseMessage response = await CreateAsync(Body());

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await _client.GetFromJsonAsync<JsonElement>($"/api/bots/{_scenario.BotId}/backtests")).GetArrayLength().ShouldBe(0);
    }

    // Scenario "Список бэктестов бота".
    [Test]
    public async Task Backtests_of_a_bot_are_listed_newest_first()
    {
        Guid first = (await (await CreateAsync(Body())).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        Guid second = (await (await CreateAsync(Body())).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        JsonElement list = await _client.GetFromJsonAsync<JsonElement>($"/api/bots/{_scenario.BotId}/backtests");

        list.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldBe([second, first]);
        list[0].GetProperty("progress").GetProperty("totalCombinations").GetInt64().ShouldBe(3);
    }

    // Scenarios "Отмена завершённого" and the cancel of a pending backtest.
    [Test]
    public async Task Unfinished_backtest_is_cancelled_and_a_finished_one_is_a_conflict()
    {
        Guid id = (await (await CreateAsync(Body())).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await _scenario.WaitUntilFinishedAsync(id);

        HttpResponseMessage response = await _client.PostAsync($"/api/backtests/{id}/cancel", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // Scenario "Правка стратегии".
    [Test]
    public async Task Backtest_shows_the_bot_changed_after_its_strategy_got_a_new_version()
    {
        Guid id = (await (await CreateAsync(Body())).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{id}")).GetProperty("botChanged").GetBoolean().ShouldBeFalse();
        JsonNode changed = JsonNode.Parse(StrategyFixtures.Json("ema"))!;
        changed["parameters"]![2]!["default"] = 300;

        await _client.PutAsJsonAsync($"/api/strategies/{_scenario.StrategyId}", new { name = "ema", definition = changed });

        (await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{id}")).GetProperty("botChanged").GetBoolean().ShouldBeTrue();
    }

    // Scenario "Бот не менялся".
    [Test]
    public async Task Backtest_does_not_show_the_bot_changed_after_a_new_name_of_the_bot()
    {
        Guid id = (await (await CreateAsync(Body())).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        HttpResponseMessage renamed = await _client.PutAsJsonAsync(
            $"/api/bots/{_scenario.BotId}",
            new { name = "Новое имя", parameterValues = new { fast = 10 }, category = "linear", symbol = "BTCUSDT", interval = "60", balance = 1000 });

        renamed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{id}")).GetProperty("botChanged").GetBoolean().ShouldBeFalse();
    }

    private Task<HttpResponseMessage> CreateAsync(JsonObject body)
    {
        return _client.PostAsJsonAsync($"/api/bots/{_scenario.BotId}/backtests", body);
    }

    private static JsonObject Body()
    {
        return new JsonObject
        {
            ["from"] = "2024-01-01T00:00:00Z",
            ["to"] = "2024-02-01T00:00:00Z",
            ["parameters"] = new JsonObject { ["fast"] = new JsonObject { ["min"] = 5, ["max"] = 15, ["step"] = 5 } }
        };
    }
}