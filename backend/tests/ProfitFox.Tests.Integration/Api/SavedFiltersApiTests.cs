using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Npgsql;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirement "Сохранённые фильтры" and decision 8: the conditions are checked by the catalog of fields.</summary>
public class SavedFiltersApiTests
{
    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private NpgsqlDataSource _database = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = ProfitFoxFactory.Create(workers: 0);
        _client = _factory.CreateClient();
        _database = NpgsqlDataSource.Create(_factory.ConnectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _database.DisposeAsync();
        await _factory.DisposeAsync();
    }

    // Scenario "Условие на поле идеи": the pairs of the snapshot are not cut by it, the ideas are; no filter is saved.
    [Test]
    public async Task A_draft_with_a_condition_on_an_idea_field_counts_the_pairs_and_the_ideas_and_saves_nothing()
    {
        await SeedIdeasAsync();

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/saved-filters/fit", new { conditions = new object[] { new { field = "expectancy", @operator = ">=", value = "0.2" } } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement fit = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        (fit.GetProperty("pairs").GetInt32(), fit.GetProperty("ideas").GetInt32()).ShouldBe((2, 1));
        (await ListAsync()).ShouldBeEmpty();
    }

    // Scenario "Надёжно выше нуля": the idea of the bucket ▲ passes, the one of a pair without a bucket does not.
    [Test]
    public async Task Reliably_positive_expectancy_is_saved_and_picks_the_ideas()
    {
        Guid sol = await SeedIdeasAsync();
        HttpResponseMessage created = await _client.PostAsJsonAsync("/api/saved-filters", new
        {
            name = "Надёжные",
            place = "ideas",
            conditions = new object[] { new { field = "expectancy", @operator = "reliably_positive" } }
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        HttpResponseMessage ideas = await _client.GetAsync($"/api/ideas?savedFilter={id}");

        ideas.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement page = JsonDocument.Parse(await ideas.Content.ReadAsStringAsync()).RootElement;
        page.GetProperty("items").EnumerateArray().Select(idea => idea.GetProperty("id").GetGuid()).ShouldBe([sol]);
        JsonElement condition = (await ListAsync()).Single().GetProperty("conditions")[0];
        (condition.GetProperty("field").GetString(), condition.GetProperty("operator").GetString()).ShouldBe(("expectancy", "reliably_positive"));
    }

    // Two pairs in the snapshot, an idea on each, and a bucket ▲ of 400 cases only for the linear one; the id of its idea.
    private async Task<Guid> SeedIdeasAsync()
    {
        var snapshotAt = DateTimeOffset.Parse("2024-03-10T12:29:10Z", CultureInfo.InvariantCulture);
        var sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
        var eth = new Instrument(MarketCategory.Spot, "ETHUSDT");
        var holder = Guid.NewGuid();
        await new ScannerStateRepository(_database).ClaimAsync(holder, snapshotAt, snapshotAt.AddSeconds(30), CancellationToken.None);
        await new MarketSnapshotRepository(_database).ReplaceAsync(
            holder,
            [.. new[] { sol, eth }.Select(instrument => new MarketSnapshotRow(instrument, snapshotAt, new PairMetrics(100m, null, null, null, null, null, null, null, null, null, null, null, null), false))],
            snapshotAt,
            CancellationToken.None);
        Idea linear = IdeaGenerator.Generate(snapshotAt.AddMinutes(-20), instrument: sol);
        await new IdeaRepository(_database).SaveAsync([linear, IdeaGenerator.Generate(snapshotAt.AddMinutes(-20), instrument: eth)], [], CancellationToken.None);
        var key = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.FifteenMinutes, IdeaSide.Long, MarketCategory.Linear);
        await new IdeaBucketRepository(_database).ReplaceAsync(IdeasEngineVersion.Current, [new IdeaBucket(key, 90, IdeaViewGenerator.WithGross(BucketStatisticsCalculator.Compute(180, 200, 20, 100m, 1000m)))], [], null, CancellationToken.None);

        return linear.Id;
    }

    [Test]
    public async Task A_draft_with_a_wrong_condition_is_refused_like_a_save()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/saved-filters/fit", new { conditions = new object[] { new { field = "volatility", @operator = ">", value = "1" } } });

        (await ErrorsAsync(response)).ShouldContainKey("conditions[0].field");
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
            name = "Надёжные",
            place = "ideas",
            conditions = new object[] { new { field = "expectancy", @operator = "reliably_positive" }, new { field = "side", @operator = "=", value = "short" } }
        });
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        JsonElement updated = (await ListAsync()).Single();
        (updated.GetProperty("name").GetString(), updated.GetProperty("place").GetString()).ShouldBe(("Надёжные", "ideas"));
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
    [TestCase("Ф", "market", "change1h", "reliably_positive", null, null, "conditions[0].operator")]
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
            conditions = new object[] { new { field = "change1h", @operator = ">", value = "1", to = "5" }, new { field = "expectancy", @operator = "reliably_positive", value = "1" } }
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
        JsonElement expectancy = fields.Single(field => field.GetProperty("code").GetString() == "expectancy");
        JsonElement side = fields.Single(field => field.GetProperty("code").GetString() == "side");
        (turnover.GetProperty("unit").GetString(), turnover.GetProperty("ofIdea").GetBoolean(), turnover.GetProperty("operators").GetArrayLength()).ShouldBe(("usdt", false, 6));
        probability.GetProperty("operators").GetArrayLength().ShouldBe(6);
        expectancy.GetProperty("operators").EnumerateArray().Select(op => op.GetString()).ShouldContain("reliably_positive");
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