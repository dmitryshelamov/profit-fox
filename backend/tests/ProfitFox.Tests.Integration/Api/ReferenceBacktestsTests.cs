using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Backtests;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>
///     Backtests of the reference strategies through the API on the hourly candle fixture, which the backtest loads from Bybit itself.
///     The expected metrics were computed beforehand by a separate calculation that follows the rules of the specs, on the instrument
///     <c>linear</c> / <c>BTCUSDT</c> with a tick of 0.01, a step and a minimum of 0.001, orders from 1, and the base rates of Bybit.
/// </summary>
public class ReferenceBacktestsTests
{
    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync();
        _factory.Bybit.Instruments.Add(("linear", "BTCUSDT"));
        _factory.Bybit.Rules[("linear", "BTCUSDT")] = ("0.01", "0.001", "0.001", "1");
        _factory.Bybit.Klines.AddRange(CandleFixtures.Hourly());
        _client = _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [TestCase("ema", """{ "fast": 2, "slow": 3 }""", "1001.75447750", "0.81947200", "0.93500550", "0.41157043", 5, 2, "50", "0.27552250", "41.66666667", "4.16666667", 1, "100.05500000")]
    [TestCase("ema-short", """{ "fast": 2, "slow": 3 }""", "999.79001650", "-0.20998350", "0", "0.41783383", 4, 2, "50", "0.21998350", "33.33333333", "4.16666667", 1, "99.94500000")]
    [TestCase("dca", "{}", "1003.89000000", "0", "3.89000000", "1.39457510", 2, 0, null, "0.11000000", "100", "66.66666667", 7, "200.11000000")]
    [TestCase("ladder", "{}", "1005.87515720", "6.90010770", "-1.02495050", "1.28297693", 11, 3, "100", "0.44124280", "87.5", "79.16666667", 8, "299.87602472")]
    public async Task Reference_strategy_gives_the_precomputed_metrics(
        string strategy,
        string values,
        string endEquity,
        string realized,
        string unrealized,
        string drawdown,
        int fills,
        int trades,
        string? winRate,
        string fees,
        string inPosition,
        string underWater,
        int longest,
        string maxPositionCost)
    {
        Guid bot = await CreateBotAsync(strategy, JsonNode.Parse(values)!);

        JsonElement result = (await RunAsync(bot, new JsonObject())).ShouldHaveSingleItem();

        JsonElement metrics = result.GetProperty("metrics");
        Money(metrics, "endEquity").ShouldBe(D(endEquity));
        Money(metrics, "pnl").ShouldBe(D(endEquity) - 1000m);
        Money(metrics, "return").ShouldBe(Math.Round((D(endEquity) - 1000m) / 10m, 8));
        Money(metrics, "realizedPnl").ShouldBe(D(realized));
        Money(metrics, "unrealizedPnl").ShouldBe(D(unrealized));
        Money(metrics, "maxDrawdown").ShouldBe(D(drawdown));
        (metrics.GetProperty("fills").GetInt32(), metrics.GetProperty("closedTrades").GetInt32()).ShouldBe((fills, trades));
        (metrics.GetProperty("winRate").ValueKind == JsonValueKind.Null ? null : (decimal?)Money(metrics, "winRate")).ShouldBe(winRate is null ? null : D(winRate));
        Money(metrics, "fees").ShouldBe(D(fees));
        (Money(metrics, "timeInPosition"), Money(metrics, "timeUnderWater")).ShouldBe((D(inPosition), D(underWater)));
        metrics.GetProperty("longestUnderWater").GetInt32().ShouldBe(longest);
        Money(metrics, "maxPositionCost").ShouldBe(D(maxPositionCost));
        (metrics.GetProperty("skippedOrders").GetInt64(), metrics.GetProperty("candles").GetInt32()).ShouldBe((0L, 24));
    }

    // Scenario "Повторный бэктест" through the API.
    [Test]
    public async Task Ladder_grid_of_twelve_combinations_gives_the_same_results_twice()
    {
        Guid bot = await CreateBotAsync("ladder", new JsonObject());
        var grid = new JsonObject
        {
            ["priceDropForBuy"] = new JsonObject { ["min"] = 0.01, ["max"] = 0.03, ["step"] = 0.01 },
            ["priceRiseForSell"] = new JsonObject { ["min"] = 0.005, ["max"] = 0.02, ["step"] = 0.005 }
        };

        IReadOnlyList<JsonElement> first = await RunAsync(bot, grid);
        IReadOnlyList<JsonElement> second = await RunAsync(bot, (JsonObject)grid.DeepClone());

        first.Count.ShouldBe(12);
        second.Select(Comparable).ShouldBe(first.Select(Comparable));
        first.Select(Comparable).Distinct().Count().ShouldBeGreaterThan(1);
    }

    private async Task<Guid> CreateBotAsync(string strategy, JsonNode values)
    {
        HttpResponseMessage created = await _client.PostAsJsonAsync("/api/strategies", new { name = strategy, definition = StrategyFixtures.Element(strategy) });
        Guid strategyId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        HttpResponseMessage bot = await _client.PostAsJsonAsync(
            "/api/bots",
            new JsonObject
            {
                ["name"] = strategy,
                ["strategyId"] = strategyId,
                ["parameterValues"] = values,
                ["category"] = "linear",
                ["symbol"] = "BTCUSDT",
                ["interval"] = "60",
                ["balance"] = 1000
            });
        bot.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await bot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<IReadOnlyList<JsonElement>> RunAsync(Guid bot, JsonObject parameters)
    {
        HttpResponseMessage created = await _client.PostAsJsonAsync(
            $"/api/bots/{bot}/backtests",
            new JsonObject { ["from"] = "2024-03-01T00:00:00Z", ["to"] = "2024-03-02T00:00:00Z", ["parameters"] = parameters });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var waiting = Stopwatch.StartNew();
        string? status = null;

        while (status is not ("Succeeded" or "Failed" or "Cancelled") && waiting.Elapsed < TimeSpan.FromSeconds(30))
        {
            _factory.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
            status = (await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{id}")).GetProperty("status").GetString();
        }

        status.ShouldBe("Succeeded");

        return [.. (await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{id}/results?limit=1000")).GetProperty("items").EnumerateArray()];
    }

    private static string Comparable(JsonElement result)
    {
        return $"{result.GetProperty("combination")} {result.GetProperty("parameterValues")} {result.GetProperty("metrics")}";
    }

    private static decimal Money(JsonElement metrics, string name)
    {
        return Math.Round(D(metrics.GetProperty(name).GetString()!), 8);
    }

    private static decimal D(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}