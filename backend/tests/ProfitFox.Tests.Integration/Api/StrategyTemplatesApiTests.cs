using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class StrategyTemplatesApiTests
{
    private const string Templates = "/api/strategy-templates";

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Список шаблонов".
    [Test]
    public async Task List_has_fourteen_templates_with_their_code_name_description_and_direction()
    {
        JsonElement[] templates = [.. (await _client.GetFromJsonAsync<JsonElement>(Templates)).EnumerateArray()];

        templates.Length.ShouldBe(14);
        (templates[0].GetProperty("code").GetString(), templates[0].GetProperty("name").GetString(), templates[0].GetProperty("direction").GetString())
            .ShouldBe(("ema-crossover", "EMA Crossover", "long"));
        (templates[^1].GetProperty("code").GetString(), templates[^1].GetProperty("name").GetString(), templates[^1].GetProperty("direction").GetString())
            .ShouldBe(("level-trade-short", "Сделка по уровням (шорт)", "short"));
        templates.ShouldAllBe(template => !string.IsNullOrWhiteSpace(template.GetProperty("description").GetString()));
        templates[0].TryGetProperty("definition", out _).ShouldBeFalse();
    }

    // Scenario "Сетки нет".
    [Test]
    public async Task List_has_no_grid()
    {
        JsonElement templates = await _client.GetFromJsonAsync<JsonElement>(Templates);

        templates.EnumerateArray().ShouldNotContain(template => template.GetProperty("code").GetString() == "grid" || template.GetProperty("name").GetString() == "Сетка");
    }

    // Scenario "Чтение шаблона".
    [Test]
    public async Task Template_is_read_with_its_parameters_and_an_empty_layout()
    {
        JsonElement template = await _client.GetFromJsonAsync<JsonElement>($"{Templates}/rsi-reversion");

        template.GetProperty("name").GetString().ShouldBe("Отскок RSI");
        JsonElement definition = template.GetProperty("definition");
        definition.GetProperty("direction").GetString().ShouldBe("long");
        definition.GetProperty("parameters").EnumerateArray()
            .ToDictionary(parameter => parameter.GetProperty("name").GetString()!, parameter => parameter.GetProperty("default").GetDecimal())
            .ShouldBe(new Dictionary<string, decimal> { ["rsiPeriod"] = 14m, ["rsiEntry"] = 30m, ["rsiExit"] = 70m, ["stopLoss"] = 0.05m, ["amount"] = 100m }, ignoreOrder: true);
        definition.GetProperty("layout").EnumerateObject().ShouldBeEmpty();
    }

    // Scenario "Неизвестный шаблон".
    [Test]
    public async Task Unknown_template_is_not_found()
    {
        (await _client.GetAsync($"{Templates}/grid")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Scenario "Сохранение каждого шаблона".
    [Test]
    public async Task Every_template_is_saved_as_a_strategy_and_read_back_unchanged()
    {
        JsonElement[] templates = [.. (await _client.GetFromJsonAsync<JsonElement>(Templates)).EnumerateArray()];

        foreach (JsonElement summary in templates)
        {
            string code = summary.GetProperty("code").GetString()!;
            JsonElement template = await _client.GetFromJsonAsync<JsonElement>($"{Templates}/{code}");

            HttpResponseMessage created = await _client.PostAsJsonAsync("/api/strategies", new { name = template.GetProperty("name").GetString(), definition = template.GetProperty("definition") });

            created.StatusCode.ShouldBe(HttpStatusCode.Created, code);
            Guid id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            JsonElement strategy = await _client.GetFromJsonAsync<JsonElement>($"/api/strategies/{id}");
            JsonNode.DeepEquals(JsonNode.Parse(strategy.GetProperty("definition").GetRawText()), JsonNode.Parse(template.GetProperty("definition").GetRawText())).ShouldBeTrue(code);
        }
    }

    // Scenario "Правка стратегии из шаблона".
    [Test]
    public async Task Strategy_made_from_a_template_is_edited_without_the_rules_of_the_template_and_the_template_stays()
    {
        JsonElement template = await _client.GetFromJsonAsync<JsonElement>($"{Templates}/ema-crossover");
        Guid id = (await (await _client.PostAsJsonAsync("/api/strategies", new { name = "Моя EMA", definition = template.GetProperty("definition") })).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
        JsonNode edited = JsonNode.Parse(template.GetProperty("definition").GetRawText())!;
        edited["parameters"]!.AsArray().Single(parameter => parameter!["name"]!.GetValue<string>() == "fast")!["default"] = 30;
        edited["parameters"]!.AsArray().Single(parameter => parameter!["name"]!.GetValue<string>() == "slow")!["default"] = 10;

        (await _client.PutAsJsonAsync($"/api/strategies/{id}", new { name = "Моя EMA", definition = edited })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        JsonElement[] parameters = [.. (await _client.GetFromJsonAsync<JsonElement>($"{Templates}/ema-crossover")).GetProperty("definition").GetProperty("parameters").EnumerateArray()];
        parameters.Single(parameter => parameter.GetProperty("name").GetString() == "fast").GetProperty("default").GetDecimal().ShouldBe(12m);
        parameters.Single(parameter => parameter.GetProperty("name").GetString() == "slow").GetProperty("default").GetDecimal().ShouldBe(26m);
    }
}