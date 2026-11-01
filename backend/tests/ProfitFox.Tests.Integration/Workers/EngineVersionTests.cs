using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Backtests.Commands.RunNextBacktestWork;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Integration.Api;
using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Workers;

/// <summary>Requirements "Версия движка" and "Бэктест старой версии движка".</summary>
public class EngineVersionTests
{
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-03-01T00:00:00Z");
    private static readonly decimal[] Closes = [100m, 98m, 97m, 99m, 101m, 100m, 98m, 96m, 97m, 99m, 102m, 103m];

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private BacktestScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync(workers: 0);
        _client = _factory.CreateClient();
        _scenario = new BacktestScenario(_factory, _client);
        await _scenario.CreateBotAsync("ladder");
        await _scenario.StoreCandlesAsync(From, Closes);
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Worker старой версии".
    [Test]
    public async Task Worker_of_an_older_engine_version_leaves_the_backtest_pending()
    {
        Backtest backtest = await _scenario.CreateAsync(From, From.AddHours(Closes.Length), engineVersion: 3);
        await using WorkerHost worker = _factory.StartWorker(OfEngineVersion(2));

        await AdvanceAsync(TimeSpan.FromMinutes(1));

        Backtest waiting = await _scenario.GetAsync(backtest.Id);
        (waiting.Status, waiting.CalculatedCombinations).ShouldBe((BacktestStatus.Pending, 0L));
    }

    // Scenario "Деплой новой версии движка посреди бэктеста".
    [Test]
    public async Task Worker_of_a_newer_engine_version_fails_an_unfinished_backtest_of_an_older_one_and_keeps_its_results()
    {
        Backtest backtest = await _scenario.CreateAsync(From, From.AddHours(Closes.Length), Grid(), engineVersion: 2);
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            IBacktestRepository stored = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();
            IBacktestRepository spy = BacktestScenario.Spy(stored, (saved, _) =>
            {
                if (saved?.CalculatedCombinations == 5)
                {
                    throw new OperationCanceledException("The Worker of version 2 stops.");
                }
            });
            var time = new FakeTimeProvider(_factory.Time.GetUtcNow()) { AutoAdvanceAmount = TimeSpan.FromSeconds(1) };
            await Should.ThrowAsync<OperationCanceledException>(() => BacktestScenario.Handler(scope, spy, time, engine: new EngineVersion(2))
                .Handle(new RunNextBacktestWorkCommand(), CancellationToken.None));
        }

        await using WorkerHost worker = _factory.StartWorker(OfEngineVersion(3));
        Backtest failed = await _scenario.WaitUntilFinishedAsync(backtest.Id);

        (failed.Status, failed.Error).ShouldBe((BacktestStatus.Failed, Backtest.EngineVersionChanged));
        (await _scenario.ResultsAsync(failed)).Select(result => result.Combination).ShouldBe([0L, 1L, 2L, 3L, 4L]);
    }

    [Test]
    public async Task Worker_does_not_touch_a_backtest_of_a_newer_engine_version()
    {
        Backtest backtest = await _scenario.CreateAsync(From, From.AddHours(Closes.Length), engineVersion: 3);
        Backtest older = await _scenario.CreateAsync(From, From.AddHours(Closes.Length), engineVersion: 1);
        await using WorkerHost worker = _factory.StartWorker(OfEngineVersion(2));

        await _scenario.WaitUntilFinishedAsync(older.Id);

        (await _scenario.GetAsync(backtest.Id)).Status.ShouldBe(BacktestStatus.Pending);
    }

    private static Action<IServiceCollection> OfEngineVersion(int number)
    {
        return services => services.AddSingleton(new EngineVersion(number));
    }

    private async Task AdvanceAsync(TimeSpan span)
    {
        for (TimeSpan passed = TimeSpan.Zero; passed < span; passed += TimeSpan.FromSeconds(1))
        {
            _factory.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10);
        }
    }

    private static Dictionary<string, ParameterSpec> Grid()
    {
        return new Dictionary<string, ParameterSpec>
        {
            ["priceDropForBuy"] = ParameterSpec.Between(new NumberValue(0.01m), new NumberValue(0.03m), new NumberValue(0.01m)),
            ["priceRiseForSell"] = ParameterSpec.Between(new NumberValue(0.005m), new NumberValue(0.02m), new NumberValue(0.005m))
        };
    }
}