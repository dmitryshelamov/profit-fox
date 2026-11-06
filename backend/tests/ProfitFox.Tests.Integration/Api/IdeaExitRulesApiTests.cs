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
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirements "Чтение правил выхода", "Расчёт кандидата", "Применение правила через API" and "Чтение идей" of tune-idea-rules.</summary>
public class IdeaExitRulesApiTests
{
    private const string Url = "/api/ideas/exit-rules";

    // The clock of the app reads 2024-03-10T12:30Z.
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z", CultureInfo.InvariantCulture);

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private NpgsqlDataSource _database = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync(workers: 0);
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

    // Scenario "Правило по умолчанию в ответе": every type × interval, the grid of each interval.
    [Test]
    public async Task Rule_never_applied_is_the_default_in_the_answer()
    {
        JsonElement answer = await _client.GetFromJsonAsync<JsonElement>(Url);

        JsonElement[] rules = [.. answer.GetProperty("rules").EnumerateArray()];
        rules.Length.ShouldBe(20);
        JsonElement pullback = rules.Single(rule => rule.GetProperty("type").GetString() == "pullback" && rule.GetProperty("interval").GetString() == "15");
        (pullback.GetProperty("life").GetInt32(), pullback.GetProperty("target").GetString(), pullback.GetProperty("window").GetInt32()).ShouldBe((24, "2", 4));
        pullback.GetProperty("appliedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        JsonElement levels = rules.Single(rule => rule.GetProperty("type").GetString() == "levels" && rule.GetProperty("interval").GetString() == "60");
        (levels.GetProperty("target").ValueKind, levels.GetProperty("window").ValueKind).ShouldBe((JsonValueKind.Null, JsonValueKind.Null));
        answer.GetProperty("grids").EnumerateArray().Single(grid => grid.GetProperty("interval").GetString() == "240").GetProperty("candles")
            .EnumerateArray().Select(candles => candles.GetInt32()).ShouldBe([1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 42]);
    }

    // Scenario "Применение": the rule saved with the time it was applied, read back by the list.
    [Test]
    public async Task Applied_rule_is_saved_with_the_time_it_was_applied()
    {
        HttpResponseMessage response = await _client.PutAsJsonAsync($"{Url}/pullback/15", new { life = 48, target = 2.5m, window = 8 });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        (saved.GetProperty("life").GetInt32(), saved.GetProperty("target").GetString(), saved.GetProperty("window").GetInt32()).ShouldBe((48, "2.5", 8));
        saved.GetProperty("appliedAt").GetDateTimeOffset().ShouldBe(Now);
        (await new IdeaExitRuleRepository(_database).GetAllAsync(CancellationToken.None))
            .ShouldBe([new IdeaExitRule(IdeaType.Pullback, Interval.FifteenMinutes, 48, 2.5m, 8, Now)]);
    }

    [Test]
    public async Task Rule_off_the_grid_is_not_applied()
    {
        HttpResponseMessage response = await _client.PutAsJsonAsync($"{Url}/target_2r/15", new { life = 24, target = 2.3m });

        await ShouldBeRefusedOnAsync(response, "target");
        (await new IdeaExitRuleRepository(_database).GetAllAsync(CancellationToken.None)).ShouldBeEmpty();
        await ShouldBeRefusedOnAsync(await _client.PutAsJsonAsync($"{Url}/unknown/15", new { life = 24 }), "type");
        await ShouldBeRefusedOnAsync(await _client.PutAsJsonAsync($"{Url}/levels/30", new { life = 24 }), "interval");
    }

    // Scenario "Окно у «Цели 2R»".
    [Test]
    public async Task Preview_with_a_window_of_a_market_type_is_refused_by_it()
    {
        await ShouldBeRefusedOnAsync(await _client.GetAsync($"{Url}/preview?type=target_2r&interval=15&life=24&target=2&window=4"), "window");
    }

    // Scenario "Срок вне сетки": 40 candles of 15 minutes are off the grid.
    [Test]
    public async Task Preview_with_a_lifetime_off_the_grid_is_refused_by_it()
    {
        await ShouldBeRefusedOnAsync(await _client.GetAsync($"{Url}/preview?type=target_2r&interval=15&life=40&target=2"), "life");
    }

    // Requirement "Расчёт кандидата": a stop and a target of +2 R at 30 minutes, which the lifetime of one candle closes as expired at 100.
    [Test]
    public async Task Preview_compares_the_rule_now_and_the_candidate_in_decimal_strings()
    {
        await new IdeaSignalRepository(_database).UpsertAsync(
            [IdeaSignalGenerator.Generate(Now.AddDays(-2)), IdeaSignalGenerator.Generate(Now.AddDays(-1), IdeaStatus.Stop, setup: IdeaSetup.Drop)],
            CancellationToken.None);

        JsonElement preview = await _client.GetFromJsonAsync<JsonElement>($"{Url}/preview?type=target_2r&interval=15&life=1&target=2");

        JsonElement last90 = preview.GetProperty("last90");
        last90.GetProperty("current").GetProperty("cases").GetInt32().ShouldBe(2);
        (last90.GetProperty("current").GetProperty("targets").GetInt32(), last90.GetProperty("candidate").GetProperty("targets").GetInt32()).ShouldBe((1, 0));
        last90.GetProperty("candidate").GetProperty("signals").GetInt32().ShouldBe(2);
        preview.GetProperty("lives").GetArrayLength().ShouldBe(ExitRuleGrid.Of(Interval.FifteenMinutes).Count);
        preview.GetProperty("mfe")[0].GetProperty("r").GetString().ShouldBe("0.5");
    }

    // Scenario "Корзина «Отката» в ответе": 200 signals counted, 100 entries with −6 R.
    [Test]
    public async Task Bucket_of_a_pullback_in_the_list_of_ideas_has_its_signals()
    {
        await new ScannerStateRepository(_database).ClaimAsync(Guid.NewGuid(), Now.AddSeconds(-50), Now.AddSeconds(-20), CancellationToken.None);
        Idea idea = IdeaGenerator.Generate(Now.AddMinutes(-20), instrument: new Instrument(MarketCategory.Linear, "SOLUSDT"), type: IdeaType.Pullback);
        await new IdeaRepository(_database).SaveAsync([idea], [], CancellationToken.None);
        var key = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.Pullback, Interval.FifteenMinutes, IdeaSide.Long, MarketCategory.Linear);
        await new IdeaBucketRepository(_database).ReplaceAsync(
            IdeasEngineVersion.Current,
            [new IdeaBucket(key, 90, BucketStatisticsCalculator.Compute(30, 60, 10, -6m, 200m, 200))],
            null,
            CancellationToken.None);

        JsonElement bucket = (await _client.GetFromJsonAsync<JsonElement>("/api/ideas")).GetProperty("items")[0].GetProperty("bucket");

        (bucket.GetProperty("signals").GetInt32(), bucket.GetProperty("entryShare").GetString(), bucket.GetProperty("signalExpectancy").GetString())
            .ShouldBe((200, "0.5", "-0.03"));
    }

    private static async Task ShouldBeRefusedOnAsync(HttpResponseMessage response, string field)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        errors.EnumerateObject().Select(error => error.Name).ShouldBe([field]);
    }
}