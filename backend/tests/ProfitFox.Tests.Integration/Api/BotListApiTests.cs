using System.Net.Http.Json;
using System.Text.Json;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirement "Чтение ботов": the list of bots with the balance and the last backtest of each.</summary>
public class BotListApiTests
{
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private BacktestScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        // No Worker: the tests seed backtests in the status they read, and a Worker would finish a seeded Running one.
        _factory = await ProfitFoxFactory.CreateAsync(workers: 0);
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

    // Scenario "Последний бэктест в списке".
    [Test]
    public async Task List_gives_the_balance_and_the_last_backtest_of_the_bot_with_its_progress()
    {
        await _scenario.CreateAsync(From, From.AddDays(1), status: BacktestStatus.Succeeded);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var parameters = new Dictionary<string, ParameterSpec> { ["fast"] = ParameterSpec.Between(Values.Number(5m), Values.Number(16m), Values.Number(1m)) };
        Backtest last = await _scenario.CreateAsync(From, From.AddDays(1), parameters, BacktestStatus.Running, calculatedCombinations: 5);

        JsonElement item = (await _client.GetFromJsonAsync<JsonElement>("/api/bots")).EnumerateArray().ShouldHaveSingleItem();

        item.GetProperty("balance").GetString().ShouldBe("1000");
        JsonElement backtest = item.GetProperty("lastBacktest");
        (backtest.GetProperty("id").GetGuid(), backtest.GetProperty("status").GetString()).ShouldBe((last.Id, "Running"));
        (backtest.GetProperty("progress").GetProperty("calculatedCombinations").GetInt64(), backtest.GetProperty("progress").GetProperty("totalCombinations").GetInt64()).ShouldBe((5, 12));
    }

    [Test]
    public async Task List_gives_each_bot_its_own_last_backtest()
    {
        var other = new BacktestScenario(_factory, _client);
        await other.CreateBotAsync("ema");
        await _scenario.CreateAsync(From, From.AddDays(1), status: BacktestStatus.Succeeded);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        Backtest lastOfFirst = await _scenario.CreateAsync(From, From.AddDays(1), status: BacktestStatus.Succeeded);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await other.CreateAsync(From, From.AddDays(1), status: BacktestStatus.Succeeded);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        Backtest lastOfOther = await other.CreateAsync(From, From.AddDays(1), status: BacktestStatus.Succeeded);

        JsonElement[] items = (await _client.GetFromJsonAsync<JsonElement>("/api/bots")).EnumerateArray().ToArray();

        items.Select(item => (item.GetProperty("id").GetGuid(), item.GetProperty("lastBacktest").GetProperty("id").GetGuid()))
            .ShouldBe([(_scenario.BotId, lastOfFirst.Id), (other.BotId, lastOfOther.Id)], ignoreOrder: true);
    }

    [Test]
    public async Task List_takes_the_backtest_with_the_greater_id_when_two_were_created_at_the_same_moment()
    {
        await _scenario.CreateAsync(From, From.AddDays(1), status: BacktestStatus.Succeeded, id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        Backtest greater = await _scenario.CreateAsync(From, From.AddDays(1), status: BacktestStatus.Succeeded, id: Guid.Parse("ffffffff-0000-0000-0000-000000000000"));

        JsonElement item = (await _client.GetFromJsonAsync<JsonElement>("/api/bots")).EnumerateArray().ShouldHaveSingleItem();

        item.GetProperty("lastBacktest").GetProperty("id").GetGuid().ShouldBe(greater.Id);
    }

    // Scenario "Бот без бэктестов в списке".
    [Test]
    public async Task List_gives_the_balance_and_no_last_backtest_of_a_bot_without_backtests()
    {
        JsonElement item = (await _client.GetFromJsonAsync<JsonElement>("/api/bots")).EnumerateArray().ShouldHaveSingleItem();

        item.GetProperty("balance").GetString().ShouldBe("1000");
        item.GetProperty("lastBacktest").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}