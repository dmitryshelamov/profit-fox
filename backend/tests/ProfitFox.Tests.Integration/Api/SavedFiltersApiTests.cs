using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirement "Сохранённые фильтры" and decision 8: the conditions are checked by the catalog of fields.</summary>
public class SavedFiltersApiTests
{
    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync(workers: 0);
        _client = _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task Filter_is_created_read_changed_and_deleted()
    {
        HttpResponseMessage created = await _client.PostAsJsonAsync("/api/saved-filters", new
        {
            name = "Шорт перегретых",
            place = "market",
            conditions = new object[] { new { field = "change1h", @operator = ">", value = "3" }, new { field = "fundingRate", @operator = "between", value = "0.0003", to = "0.001" } }
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        JsonElement filter = (await ListAsync()).Single();
        (filter.GetProperty("id").GetGuid(), filter.GetProperty("name").GetString(), filter.GetProperty("place").GetString()).ShouldBe((id, "Шорт перегретых", "market"));
        JsonElement[] conditions = [.. filter.GetProperty("conditions").EnumerateArray()];
        (conditions[0].GetProperty("field").GetString(), conditions[0].GetProperty("operator").GetString(), conditions[0].GetProperty("value").GetString()).ShouldBe(("change1h", ">", "3"));
        (conditions[1].GetProperty("value").GetString(), conditions[1].GetProperty("to").GetString()).ShouldBe(("0.0003", "0.001"));

        HttpResponseMessage changed = await _client.PutAsJsonAsync($"/api/saved-filters/{id}", new
        {
            name = "Выше базы",
            place = "ideas",
            conditions = new object[] { new { field = "probability", @operator = "above_baseline" }, new { field = "side", @operator = "=", value = "short" } }
        });
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        JsonElement updated = (await ListAsync()).Single();
        (updated.GetProperty("name").GetString(), updated.GetProperty("place").GetString()).ShouldBe(("Выше базы", "ideas"));
        updated.GetProperty("conditions")[0].GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        (updated.GetProperty("conditions")[1].GetProperty("field").GetString(), updated.GetProperty("conditions")[1].GetProperty("value").GetString()).ShouldBe(("side", "short"));

        (await _client.DeleteAsync($"/api/saved-filters/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync()).ShouldBeEmpty();
        (await _client.DeleteAsync($"/api/saved-filters/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.PutAsJsonAsync($"/api/saved-filters/{id}", new { name = "Нет", place = "both", conditions = new object[] { new { field = "change1h", @operator = ">", value = "1" } } }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Scenario "Фильтр без условий".
    [Test]
    public async Task Filter_without_conditions_is_refused()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/saved-filters", new { name = "Пустой", place = "market", conditions = Array.Empty<object>() });

        (await ErrorsAsync(response)).ShouldContainKey("conditions");
        (await ListAsync()).ShouldBeEmpty();
    }

    [TestCase("", "market", "change1h", ">", "1", null, "name")]
    [TestCase("Ф", "everywhere", "change1h", ">", "1", null, "place")]
    [TestCase("Ф", "market", "volatility", ">", "1", null, "conditions[0].field")]
    [TestCase("Ф", "market", "change1h", "above_baseline", null, null, "conditions[0].operator")]
    [TestCase("Ф", "market", "price", ">", "1", null, "conditions[0].field")]
    [TestCase("Ф", "market", "cases", ">", "1", null, "conditions[0].field")]
    [TestCase("Ф", "market", "aboveBaseline", "above_baseline", null, null, "conditions[0].field")]
    [TestCase("Ф", "market", "change1h", ">", "много", null, "conditions[0].value")]
    [TestCase("Ф", "market", "side", "=", "up", null, "conditions[0].value")]
    [TestCase("Ф", "market", "side", ">", "short", null, "conditions[0].operator")]
    [TestCase("Ф", "market", "category", "=", "inverse", null, "conditions[0].value")]
    [TestCase("Ф", "market", "setup", "=", "rocket", null, "conditions[0].value")]
    [TestCase("Ф", "market", "ideaSetup", "=", "rocket", null, "conditions[0].value")]
    [TestCase("Ф", "market", "type", "=", "scalp", null, "conditions[0].value")]
    [TestCase("Ф", "market", "interval", "=", "30", null, "conditions[0].value")]
    [TestCase("Ф", "market", "change1h", ">", "3,5", null, "conditions[0].value")]
    [TestCase("Ф", "market", "turnover24h", ">", "1,000", null, "conditions[0].value")]
    [TestCase("Ф", "market", "change1h", "between", "1", null, "conditions[0].to")]
    public async Task Invalid_filter_names_what_is_wrong(string name, string place, string field, string op, string? value, string? to, string key)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/saved-filters", new { name, place, conditions = new object[] { new { field, @operator = op, value, to } } });

        (await ErrorsAsync(response)).ShouldContainKey(key);
    }

    // A value the operator does not take is not kept.
    [Test]
    public async Task Values_an_operator_does_not_take_are_left_out()
    {
        await _client.PostAsJsonAsync("/api/saved-filters", new
        {
            name = "Ф",
            place = "both",
            conditions = new object[] { new { field = "change1h", @operator = ">", value = "1", to = "5" }, new { field = "probability", @operator = "above_baseline", value = "1" } }
        });

        JsonElement[] conditions = [.. (await ListAsync()).Single().GetProperty("conditions").EnumerateArray()];
        conditions[0].GetProperty("to").ValueKind.ShouldBe(JsonValueKind.Null);
        conditions[1].GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Catalog_tells_each_field_with_its_unit_and_operators()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/saved-filters/fields");

        JsonElement[] fields = [.. JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.EnumerateArray()];
        JsonElement turnover = fields.Single(field => field.GetProperty("code").GetString() == "turnover24h");
        JsonElement probability = fields.Single(field => field.GetProperty("code").GetString() == "probability");
        JsonElement side = fields.Single(field => field.GetProperty("code").GetString() == "side");
        (turnover.GetProperty("unit").GetString(), turnover.GetProperty("ofIdea").GetBoolean(), turnover.GetProperty("operators").GetArrayLength()).ShouldBe(("usdt", false, 6));
        probability.GetProperty("operators").EnumerateArray().Select(op => op.GetString()).ShouldContain("above_baseline");
        (side.GetProperty("unit").GetString(), side.GetProperty("ofIdea").GetBoolean(), side.GetProperty("operators")[0].GetString()).ShouldBe(("choice", true, "="));
        side.GetProperty("choices").EnumerateArray().Select(choice => choice.GetString()).ShouldBe(["long", "short"]);
        // The fields of the board «Фильтр — редактор».
        fields.Where(field => !field.GetProperty("ofIdea").GetBoolean()).Select(field => field.GetProperty("code").GetString()).ShouldBe(
        [
            "category", "change5m", "change15m", "change1h", "change4h", "change24h", "turnover1h", "turnover24h", "openInterest", "openInterestChange1h",
            "openInterestChange24h", "fundingRate", "spread", "setup"
        ]);
        fields.Where(field => field.GetProperty("ofIdea").GetBoolean()).Select(field => field.GetProperty("code").GetString())
            .ShouldBe(["ideaSetup", "type", "interval", "side", "riskReward", "probability", "expectancy"]);
    }

    private async Task<JsonElement[]> ListAsync()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/saved-filters");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return [.. JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.EnumerateArray()];
    }

    private static async Task<Dictionary<string, JsonElement>> ErrorsAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").EnumerateObject().ToDictionary(error => error.Name, error => error.Value);
    }
}