using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirements of backtest-runs on running a backtest in the background.</summary>
public class BacktestRunTests
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
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Успешный бэктест".
    [Test]
    public async Task Backtest_with_every_candle_stored_succeeds_with_a_result_per_combination()
    {
        await _scenario.StoreCandlesAsync(From, 10m, 9m, 8m, 7m, 8m, 10m, 12m, 11m, 9m, 7m);

        Backtest queued = await _scenario.QueueAsync(
            From,
            From.AddHours(10),
            new Dictionary<string, ParameterSpec> { ["fast"] = ParameterSpec.Between(new NumberValue(2m), new NumberValue(3m), new NumberValue(1m)) });
        Backtest backtest = await _scenario.WaitUntilFinishedAsync(queued.Id);

        backtest.Status.ShouldBe(BacktestStatus.Succeeded);
        (backtest.ActualStart, backtest.Gaps.Count, backtest.CalculatedCombinations, backtest.TotalCombinations).ShouldBe((From, 0, 2L, 2L));
        backtest.FinishedAt.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(backtest.StartedAt.ShouldNotBeNull());
        IReadOnlyList<BacktestResult> results = await _scenario.ResultsAsync(backtest);
        results.Select(result => result.Combination).ShouldBe([0L, 1L]);
        results[0].ParameterValues["fast"].ShouldBe(new NumberValue(2m));
        results[0].Metrics.ClosedTrades.ShouldBe(1);
        results[0].Metrics.Candles.ShouldBe(10);
    }
}