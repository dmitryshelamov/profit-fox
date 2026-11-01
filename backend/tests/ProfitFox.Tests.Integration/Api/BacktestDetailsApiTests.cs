using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirement "Детали результата".</summary>
public class BacktestDetailsApiTests
{
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-03-01T00:00:00Z");

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private BacktestScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync();
        _client = _factory.CreateClient();
        _scenario = new BacktestScenario(_factory, _client);
        await _scenario.CreateBotAsync("ema", new { fast = 2, slow = 3 });
        await _scenario.StoreCandlesAsync(From, 10m, 9m, 8m, 7m, 8m, 10m, 12m, 11m, 9m, 7m, 8m);
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Детали после правки стратегии".
    [Test]
    public async Task Details_are_computed_at_the_version_of_the_backtest_and_match_its_result()
    {
        Backtest queued = await _scenario.QueueAsync(From, From.AddHours(11));
        Backtest backtest = await _scenario.WaitUntilFinishedAsync(queued.Id);
        JsonNode changed = JsonNode.Parse(StrategyFixtures.Json("ema"))!;
        changed["parameters"]![2]!["default"] = 300;
        (await _client.PutAsJsonAsync($"/api/strategies/{_scenario.StrategyId}", new { name = "ema", definition = changed })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        JsonElement result = (await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{backtest.Id}/results")).GetProperty("items")[0];

        JsonElement details = await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{backtest.Id}/results/0/details");

        JsonNode.DeepEquals(JsonNode.Parse(details.GetProperty("metrics").GetRawText()), JsonNode.Parse(result.GetProperty("metrics").GetRawText())).ShouldBeTrue();
        JsonElement[] fills = [.. details.GetProperty("fills").EnumerateArray()];
        fills.Select(fill => (fill.GetProperty("side").GetString(), fill.GetProperty("type").GetString())).ShouldBe([("buy", "market"), ("sell", "market")]);
        fills[0].GetProperty("at").GetDateTimeOffset().ShouldBe(From.AddHours(6));
        Number(fills[0], "price").ShouldBe(10m);
        Number(fills[0], "quantity").ShouldBe(10m);
        fills[0].GetProperty("feeAsset").GetString().ShouldBe("quote");
        JsonElement trade = details.GetProperty("trades").EnumerateArray().ShouldHaveSingleItem();
        Number(trade, "pnl").ShouldBe(Number(result.GetProperty("metrics"), "pnl"));
        (Number(trade, "entryPrice"), Number(trade, "exitPrice")).ShouldBe((10m, 9m));
        JsonElement[] equity = [.. details.GetProperty("equity").EnumerateArray()];
        equity.Length.ShouldBe(11);
        Number(equity[^1], "equity").ShouldBe(Number(result.GetProperty("metrics"), "endEquity"));
    }

    [Test]
    public async Task Trade_open_at_the_end_has_no_close_exit_or_result()
    {
        Backtest queued = await _scenario.QueueAsync(From, From.AddHours(8));
        await _scenario.WaitUntilFinishedAsync(queued.Id);

        JsonElement trade = (await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{queued.Id}/results/0/details")).GetProperty("trades").EnumerateArray().ShouldHaveSingleItem();

        (trade.GetProperty("closedAt").ValueKind, trade.GetProperty("exitPrice").ValueKind, trade.GetProperty("pnl").ValueKind)
            .ShouldBe((JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null));
    }

    [Test]
    public async Task Details_of_an_unknown_backtest_or_combination_are_not_found()
    {
        Backtest queued = await _scenario.QueueAsync(From, From.AddHours(11));
        await _scenario.WaitUntilFinishedAsync(queued.Id);

        (await _client.GetAsync($"/api/backtests/{queued.Id}/results/1/details")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/backtests/{Guid.NewGuid()}/results/0/details")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static decimal Number(JsonElement item, string name)
    {
        return decimal.Parse(item.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);
    }
}