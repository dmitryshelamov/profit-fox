using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirement "Список результатов".</summary>
public class BacktestResultsApiTests
{
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-03-01T00:00:00Z");
    private static readonly decimal[] Closes = [100m, 98m, 97m, 99m, 101m, 100m, 98m, 96m, 97m, 99m, 102m, 103m, 101m, 99m, 98m, 100m];

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private Backtest _backtest = null!;
    private BacktestScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync();
        _client = _factory.CreateClient();
        _scenario = new BacktestScenario(_factory, _client);
        await _scenario.CreateBotAsync("ladder");
        await _scenario.StoreCandlesAsync(From, Closes);
        Backtest queued = await _scenario.QueueAsync(
            From,
            From.AddHours(Closes.Length),
            new Dictionary<string, ParameterSpec>
            {
                ["priceDropForBuy"] = ParameterSpec.Between(new NumberValue(0.01m), new NumberValue(0.03m), new NumberValue(0.01m)),
                ["priceRiseForSell"] = ParameterSpec.Between(new NumberValue(0.005m), new NumberValue(0.02m), new NumberValue(0.005m))
            });
        _backtest = await _scenario.WaitUntilFinishedAsync(queued.Id);
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Сортировка по доходности".
    [Test]
    public async Task Three_best_results_by_return_come_in_descending_order()
    {
        JsonElement all = await ResultsAsync("?sort=combination&order=asc&limit=100");
        decimal[] returns = [.. Items(all).Select(item => Metric(item, "return")).OrderDescending()];

        JsonElement page = await ResultsAsync("?sort=return&order=desc&limit=3");

        page.GetProperty("total").GetInt64().ShouldBe(12);
        Items(page).Select(item => Metric(item, "return")).ShouldBe(returns[..3]);
        Items(all).Select(item => item.GetProperty("combination").GetInt64()).ShouldBe([.. Enumerable.Range(0, 12).Select(number => (long)number)]);
    }

    [Test]
    public async Task Equal_values_go_by_combination_and_pages_follow_each_other()
    {
        JsonElement first = await ResultsAsync("?sort=candles&limit=5");
        JsonElement second = await ResultsAsync("?sort=candles&offset=5&limit=5");

        Items(first).Concat(Items(second)).Select(item => item.GetProperty("combination").GetInt64()).ShouldBe([.. Enumerable.Range(0, 10).Select(number => (long)number)]);
    }

    [Test]
    public async Task Result_gives_its_parameter_values_and_metrics()
    {
        JsonElement item = Items(await ResultsAsync("?limit=1")).Single();

        item.GetProperty("parameterValues").GetProperty("priceDropForBuy").GetDecimal().ShouldBe(0.01m);
        item.GetProperty("parameterValues").GetProperty("limitPerOrder").GetDecimal().ShouldBe(100m);
        Metric(item, "startEquity").ShouldBe(1000m);
        item.GetProperty("metrics").GetProperty("candles").GetInt32().ShouldBe(16);
    }

    [TestCase("desc", new[] { 2L, 0L, 1L })]
    [TestCase("asc", new[] { 0L, 2L, 1L })]
    public async Task Missing_values_go_last(string order, long[] combinations)
    {
        Backtest backtest = await _scenario.CreateAsync(From, From.AddHours(Closes.Length));
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        IBacktestRepository stored = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();
        Backtest running = backtest with { Status = BacktestStatus.Running };
        await stored.UpdateAsync(running, BacktestStatus.Pending, CancellationToken.None);
        BacktestResult result = (await _scenario.ResultsAsync(_backtest))[0];
        await stored.SaveProgressAsync(
            running,
            [
                result with { BacktestId = backtest.Id, Combination = 0, Metrics = result.Metrics with { WinRatePercent = 50m } },
                result with { BacktestId = backtest.Id, Combination = 1, Metrics = result.Metrics with { WinRatePercent = null } },
                result with { BacktestId = backtest.Id, Combination = 2, Metrics = result.Metrics with { WinRatePercent = 80m } }
            ],
            CancellationToken.None);

        JsonElement page = await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{backtest.Id}/results?sort=winRate&order={order}");

        Items(page).Select(item => item.GetProperty("combination").GetInt64()).ShouldBe(combinations);
        Items(page).Last().GetProperty("metrics").GetProperty("winRate").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [TestCase("?sort=luck")]
    [TestCase("?order=sideways")]
    [TestCase("?limit=0")]
    [TestCase("?limit=1001")]
    [TestCase("?offset=-1")]
    public async Task Unknown_metric_order_or_page_is_rejected(string query)
    {
        (await _client.GetAsync($"/api/backtests/{_backtest.Id}/results{query}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Results_of_an_unknown_backtest_are_not_found()
    {
        (await _client.GetAsync($"/api/backtests/{Guid.NewGuid()}/results")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<JsonElement> ResultsAsync(string query)
    {
        return await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{_backtest.Id}/results{query}");
    }

    private static IEnumerable<JsonElement> Items(JsonElement page)
    {
        return page.GetProperty("items").EnumerateArray();
    }

    private static decimal Metric(JsonElement item, string name)
    {
        return decimal.Parse(item.GetProperty("metrics").GetProperty(name).GetString()!, CultureInfo.InvariantCulture);
    }
}