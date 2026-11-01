using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
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

    // Scenario "Свечей не хватает".
    [Test]
    public async Task Backtest_loads_the_missing_candles_through_an_import_job_and_then_runs()
    {
        decimal[] closes = [10m, 9m, 8m, 7m, 8m, 10m, 12m, 11m, 9m, 7m];
        await _scenario.StoreCandlesAsync(From, closes[..5]);
        _factory.Bybit.Klines.AddRange(Rows(BacktestScenario.Candles(From, closes).Skip(5)));

        Backtest queued = await _scenario.CreateAsync(From, From.AddHours(10));
        Backtest backtest = await _scenario.WaitUntilFinishedAsync(queued.Id);

        backtest.Status.ShouldBe(BacktestStatus.Succeeded);
        (backtest.LoadedCandles, backtest.ExpectedCandles, backtest.Gaps.Count).ShouldBe((10L, 10L, 0));
        JsonElement job = await _client.GetFromJsonAsync<JsonElement>($"/api/market-data/candle-imports/{backtest.ImportJobId}");
        (job.GetProperty("status").GetString(), job.GetProperty("importedCandles").GetInt64()).ShouldBe(("Succeeded", 5L));
        _factory.Bybit.KlineRequests.ShouldHaveSingleItem().Start.ShouldBe(From.AddHours(5));
        (await _scenario.ResultsAsync(backtest)).ShouldHaveSingleItem().Metrics.Candles.ShouldBe(10);
    }

    // Scenario "Загрузка упала".
    [Test]
    public async Task Backtest_fails_with_the_reason_of_a_failed_import()
    {
        _factory.Bybit.RejectKlines = true;

        Backtest queued = await _scenario.CreateAsync(From, From.AddHours(10));
        Backtest backtest = await _scenario.WaitUntilFinishedAsync(queued.Id);

        backtest.Status.ShouldBe(BacktestStatus.Failed);
        JsonElement job = await _client.GetFromJsonAsync<JsonElement>($"/api/market-data/candle-imports/{backtest.ImportJobId}");
        job.GetProperty("status").GetString().ShouldBe("Failed");
        backtest.Error.ShouldBe(job.GetProperty("error").GetString());
        backtest.Error.ShouldNotBeNullOrEmpty();
    }

    // Scenario "Период до листинга".
    [Test]
    public async Task Backtest_of_a_period_that_starts_before_the_listing_runs_from_the_first_candle_and_reports_the_gap()
    {
        _factory.Bybit.Klines.AddRange(Rows(BacktestScenario.Candles(From.AddHours(4), 10m, 9m, 8m, 9m, 10m, 11m)));

        Backtest queued = await _scenario.CreateAsync(From, From.AddHours(10));
        Backtest backtest = await _scenario.WaitUntilFinishedAsync(queued.Id);

        backtest.Status.ShouldBe(BacktestStatus.Succeeded);
        backtest.ActualStart.ShouldBe(From.AddHours(4));
        backtest.Gaps.ShouldBe([new CandleGap(From, From.AddHours(4))]);
        (backtest.LoadedCandles, backtest.ExpectedCandles).ShouldBe((6L, 10L));
        (await _scenario.ResultsAsync(backtest)).ShouldHaveSingleItem().Metrics.Candles.ShouldBe(6);
    }

    [Test]
    public async Task Backtest_of_a_period_without_any_candle_keeps_the_balance()
    {
        Backtest queued = await _scenario.CreateAsync(From, From.AddHours(10));
        Backtest backtest = await _scenario.WaitUntilFinishedAsync(queued.Id);

        backtest.Status.ShouldBe(BacktestStatus.Succeeded);
        backtest.ActualStart.ShouldBeNull();
        (await _scenario.ResultsAsync(backtest)).ShouldHaveSingleItem().Metrics.EndEquity.ShouldBe(1000m);
    }

    [Test]
    public async Task Backtest_whose_combination_fails_ends_failed_with_the_reason()
    {
        // The entry of the cross up fills at the open of the seventh candle, and a buy by amount at no price cannot be sized.
        Candle[] candles = [.. BacktestScenario.Candles(From, 10m, 9m, 8m, 7m, 8m, 10m, 12m, 11m, 9m, 7m)];
        candles[6] = candles[6] with { Open = new Price(0m) };
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICandleRepository>().UpsertAsync(BacktestScenario.BtcLinear, Interval.OneHour, candles, CancellationToken.None);
        }

        Backtest queued = await _scenario.CreateAsync(From, From.AddHours(10));
        Backtest backtest = await _scenario.WaitUntilFinishedAsync(queued.Id);

        backtest.Status.ShouldBe(BacktestStatus.Failed);
        backtest.Error.ShouldStartWith("A combination failed: ");
    }

    private static IEnumerable<string[]> Rows(IEnumerable<Candle> candles)
    {
        return candles.Select(candle => new[]
        {
            candle.OpenTime.ToString("O"),
            Text(candle.Open.Value),
            Text(candle.High.Value),
            Text(candle.Low.Value),
            Text(candle.Close.Value),
            Text(candle.Volume.Value),
            Text(candle.Turnover.Value)
        });

        static string Text(decimal value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    // Scenario "Успешный бэктест".
    [Test]
    public async Task Backtest_with_every_candle_stored_succeeds_with_a_result_per_combination()
    {
        await _scenario.StoreCandlesAsync(From, 10m, 9m, 8m, 7m, 8m, 10m, 12m, 11m, 9m, 7m);

        Backtest queued = await _scenario.CreateAsync(
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