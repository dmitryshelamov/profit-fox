using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Backtests.Commands.CancelBacktest;
using ProfitFox.Application.Backtests.Commands.RunNextBacktestWork;
using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirement "Отмена бэктеста" and the deletion of a bot while its backtest runs.</summary>
public class BacktestCancelTests
{
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-03-01T00:00:00Z");
    private static readonly decimal[] Closes = [100m, 98m, 97m, 99m, 101m, 100m, 98m, 96m, 97m, 99m, 102m, 103m];

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private BacktestScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        // The tests take the place of the Worker.
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

    // Scenario "Отмена во время прогона".
    [Test]
    public async Task Backtest_cancelled_after_five_combinations_ends_cancelled_with_those_results()
    {
        Backtest backtest = await _scenario.CreateAsync(From, From.AddHours(Closes.Length), Grid());
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        IBacktestRepository stored = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();
        var cancel = new CancelBacktestCommandHandler(stored, _factory.Time);
        IBacktestRepository spy = BacktestScenario.Spy(stored, (saved, written) =>
        {
            if (written && saved?.CalculatedCombinations == 5)
            {
                cancel.Handle(new CancelBacktestCommand(backtest.Id), CancellationToken.None).GetAwaiter().GetResult();
            }
        });

        await BacktestScenario.RunUntilIdleAsync(BacktestScenario.Handler(scope, spy, Ticking()));

        Backtest cancelled = (await stored.GetAsync(backtest.Id, CancellationToken.None))!;
        (cancelled.Status, cancelled.CalculatedCombinations).ShouldBe((BacktestStatus.Cancelled, 5L));
        (await stored.GetCalculatedCombinationsAsync(backtest.Id, CancellationToken.None)).ShouldBe([0L, 1L, 2L, 3L, 4L]);
    }

    [TestCase(BacktestStatus.Pending)]
    [TestCase(BacktestStatus.LoadingCandles)]
    public async Task Backtest_that_has_not_run_yet_is_cancelled_at_once_and_never_runs(BacktestStatus status)
    {
        Backtest backtest = await _scenario.CreateAsync(From, From.AddHours(Closes.Length));
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        IBacktestRepository stored = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();
        await stored.UpdateAsync(backtest with { Status = status }, BacktestStatus.Pending, CancellationToken.None);

        await new CancelBacktestCommandHandler(stored, _factory.Time).Handle(new CancelBacktestCommand(backtest.Id), CancellationToken.None);
        await BacktestScenario.RunUntilIdleAsync(BacktestScenario.Handler(scope, stored, Ticking()));

        Backtest cancelled = (await stored.GetAsync(backtest.Id, CancellationToken.None))!;
        (cancelled.Status, cancelled.FinishedAt).ShouldBe((BacktestStatus.Cancelled, _factory.Time.GetUtcNow()));
        (await stored.GetCalculatedCombinationsAsync(backtest.Id, CancellationToken.None)).ShouldBeEmpty();
    }

    [Test]
    public async Task Backtest_cancelled_while_it_waits_for_its_import_does_not_run_after_the_import()
    {
        Backtest backtest = await _scenario.CreateAsync(From.AddHours(-5), From.AddHours(Closes.Length));
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        IBacktestRepository stored = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();
        RunNextBacktestWorkCommandHandler worker = BacktestScenario.Handler(scope, stored, Ticking());
        await BacktestScenario.RunUntilIdleAsync(worker);
        Backtest waiting = (await stored.GetAsync(backtest.Id, CancellationToken.None))!;
        ICandleImportJobRepository jobs = scope.ServiceProvider.GetRequiredService<ICandleImportJobRepository>();
        CandleImportJob job = (await jobs.GetAsync(waiting.ImportJobId!.Value, CancellationToken.None))!;

        await new CancelBacktestCommandHandler(stored, _factory.Time).Handle(new CancelBacktestCommand(backtest.Id), CancellationToken.None);
        await jobs.UpdateAsync(job with { Status = CandleImportStatus.Succeeded }, CancellationToken.None);
        await BacktestScenario.RunUntilIdleAsync(worker);

        Backtest cancelled = (await stored.GetAsync(backtest.Id, CancellationToken.None))!;
        (waiting.Status, cancelled.Status).ShouldBe((BacktestStatus.LoadingCandles, BacktestStatus.Cancelled));
        (await stored.GetCalculatedCombinationsAsync(backtest.Id, CancellationToken.None)).ShouldBeEmpty();
    }

    // Scenario "Отмена завершённого".
    [Test]
    public async Task Finished_backtest_is_not_cancelled()
    {
        Backtest queued = await _scenario.CreateAsync(From, From.AddHours(Closes.Length));
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        IBacktestRepository stored = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();
        await BacktestScenario.RunUntilIdleAsync(BacktestScenario.Handler(scope, stored, Ticking()));
        Backtest finished = (await stored.GetAsync(queued.Id, CancellationToken.None))!;

        await Should.ThrowAsync<ConflictException>(() => new CancelBacktestCommandHandler(stored, _factory.Time).Handle(new CancelBacktestCommand(queued.Id), CancellationToken.None));

        Backtest after = (await stored.GetAsync(queued.Id, CancellationToken.None))!;
        (after.Status, after.FinishedAt).ShouldBe((BacktestStatus.Succeeded, finished.FinishedAt));
    }

    [Test]
    public async Task Unknown_backtest_is_not_found()
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        IBacktestRepository stored = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();

        await Should.ThrowAsync<NotFoundException>(() => new CancelBacktestCommandHandler(stored, _factory.Time).Handle(new CancelBacktestCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Test]
    public async Task Deleting_the_bot_stops_its_running_backtest()
    {
        Backtest backtest = await _scenario.CreateAsync(From, From.AddHours(Closes.Length), Grid());
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        IBacktestRepository stored = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();
        IStrategyLibraryRepository library = scope.ServiceProvider.GetRequiredService<IStrategyLibraryRepository>();
        var writes = new List<bool>();
        IBacktestRepository spy = BacktestScenario.Spy(stored, (saved, written) =>
        {
            writes.Add(written);

            if (saved?.CalculatedCombinations == 3)
            {
                StrategyLibrary loaded = library.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                loaded.RemoveBot(_scenario.BotId);
                library.SaveAsync(loaded.Change, loaded.Revision, CancellationToken.None).GetAwaiter().GetResult();
            }
        });

        await BacktestScenario.RunUntilIdleAsync(BacktestScenario.Handler(scope, spy, Ticking()));

        writes.ShouldBe([true, true, true, false]);
        (await stored.GetAsync(backtest.Id, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Backtest_of_a_deleted_bot_does_not_run()
    {
        Backtest backtest = await _scenario.CreateAsync(From, From.AddHours(Closes.Length));
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        IStrategyLibraryRepository library = scope.ServiceProvider.GetRequiredService<IStrategyLibraryRepository>();
        StrategyLibrary loaded = await library.LoadAsync(CancellationToken.None);
        loaded.RemoveBot(_scenario.BotId);
        await library.SaveAsync(loaded.Change, loaded.Revision, CancellationToken.None);

        bool found = await BacktestScenario.Handler(scope, scope.ServiceProvider.GetRequiredService<IBacktestRepository>(), Ticking())
            .Handle(new RunNextBacktestWorkCommand(), CancellationToken.None);

        found.ShouldBeFalse();
    }

    /// <summary>A clock that moves a second on every read, so that every result is saved on its own.</summary>
    private FakeTimeProvider Ticking()
    {
        return new FakeTimeProvider(_factory.Time.GetUtcNow()) { AutoAdvanceAmount = TimeSpan.FromSeconds(1) };
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