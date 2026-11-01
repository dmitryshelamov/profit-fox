using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Backtests.Commands.RunBacktest;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirements "Прогресс и скорость" and "Детерминизм прогона" on the threads of a backtest.</summary>
public class BacktestParallelTests
{
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-03-01T00:00:00Z");
    private static readonly decimal[] Closes = [100m, 98m, 97m, 99m, 101m, 100m, 98m, 96m, 97m, 99m, 102m, 103m, 101m, 99m, 98m, 100m];

    // Scenario "Прогресс прогона": with one thread and a clock that moves a second on every read, every result is a batch of its own.
    [Test]
    public async Task Running_backtest_saves_the_calculated_combinations_of_all_and_the_speed_in_candles_a_second()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync();
        using HttpClient client = factory.CreateClient();
        var scenario = new BacktestScenario(factory, client);
        await scenario.CreateBotAsync("ladder");
        await scenario.StoreCandlesAsync(From, Closes);
        Backtest backtest = await scenario.CreateAsync(From, From.AddHours(Closes.Length), Grid());
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IBacktestRepository stored = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();
        var progress = new List<Backtest>();
        IBacktestRepository spy = BacktestScenario.Spy(stored, (saved, _) => progress.Add(saved));
        var time = new FakeTimeProvider(factory.Time.GetUtcNow()) { AutoAdvanceAmount = TimeSpan.FromSeconds(1) };
        RunBacktestCommandHandler handler = BacktestScenario.Handler(scope, spy, time);

        await handler.Handle(new RunBacktestCommand(backtest.Id), CancellationToken.None);

        progress.Select(saved => saved.CalculatedCombinations).ShouldBe([.. Enumerable.Range(1, 12).Select(count => (long)count)]);
        Backtest fifth = progress[4];
        (fifth.Status, fifth.CalculatedCombinations, fifth.TotalCombinations).ShouldBe((BacktestStatus.Running, 5L, 12L));
        fifth.Speed.ShouldNotBeNull().ShouldBeGreaterThan(0m);
        (await stored.GetAsync(backtest.Id, CancellationToken.None))!.Status.ShouldBe(BacktestStatus.Succeeded);
    }

    // Scenario "Повторный бэктест".
    [Test]
    public async Task Ladder_grid_gives_the_same_results_with_one_and_with_four_threads()
    {
        IReadOnlyList<BacktestResult> one = await RunAsync(1);
        IReadOnlyList<BacktestResult> four = await RunAsync(4);

        one.Count.ShouldBe(12);
        four.Select(result => (result.Combination, result.Metrics)).ShouldBe(one.Select(result => (result.Combination, result.Metrics)));
        four.Select(result => result.ParameterValues).ShouldBe(one.Select(result => result.ParameterValues));
    }

    private static async Task<IReadOnlyList<BacktestResult>> RunAsync(int threads)
    {
        await using var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { BacktestThreads = threads };
        using HttpClient client = factory.CreateClient();
        var scenario = new BacktestScenario(factory, client);
        await scenario.CreateBotAsync("ladder");
        await scenario.StoreCandlesAsync(From, Closes);

        Backtest queued = await scenario.QueueAsync(From, From.AddHours(Closes.Length), Grid());
        Backtest backtest = await scenario.WaitUntilFinishedAsync(queued.Id);

        backtest.Status.ShouldBe(BacktestStatus.Succeeded);

        return await scenario.ResultsAsync(backtest);
    }

    /// <summary>Three drops by four rises: twelve combinations.</summary>
    private static Dictionary<string, ParameterSpec> Grid()
    {
        return new Dictionary<string, ParameterSpec>
        {
            ["priceDropForBuy"] = ParameterSpec.Between(new NumberValue(0.01m), new NumberValue(0.03m), new NumberValue(0.01m)),
            ["priceRiseForSell"] = ParameterSpec.Between(new NumberValue(0.005m), new NumberValue(0.02m), new NumberValue(0.005m))
        };
    }
}