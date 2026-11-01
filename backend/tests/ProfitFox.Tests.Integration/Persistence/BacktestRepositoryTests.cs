using Npgsql;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class BacktestRepositoryTests
{
    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private NpgsqlDataSource _dataSource = null!;
    private BacktestRepository _repository = null!;
    private StrategyLibraryRepository _library = null!;
    private Bot _bot = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        _repository = new BacktestRepository(_dataSource);
        _library = new StrategyLibraryRepository(_dataSource);
        var library = new StrategyLibrary(0, [], []);
        Strategy ema = library.AddStrategy(Guid.NewGuid(), "EMA", StrategyJson.ParseStrategy(StrategyFixtures.Element("ema")), Created);
        _bot = library.AddBot(Guid.NewGuid(), "EMA BTC", ema.Id, new Dictionary<string, NodeValue>(), new Instrument(MarketCategory.Linear, "BTCUSDT"), Interval.OneHour, 1000m, Created);
        await _library.SaveAsync(library.Change, 0, CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Backtest_is_read_back_without_losing_precision()
    {
        Backtest backtest = Backtest(Created) with
        {
            Status = BacktestStatus.Running,
            Error = "none",
            ImportJobId = Guid.NewGuid(),
            ActualStart = Created.AddDays(-20),
            Gaps = [new CandleGap(Created.AddDays(-30), Created.AddDays(-20))],
            LoadedCandles = 5,
            ExpectedCandles = 7,
            CalculatedCombinations = 2,
            Speed = 1234.5678901234567890m,
            StalledRestarts = 1,
            StartedAt = Created.AddMinutes(1),
            FinishedAt = Created.AddMinutes(2)
        };

        await _repository.AddAsync(backtest, CancellationToken.None);

        Backtest loaded = (await _repository.GetAsync(backtest.Id, CancellationToken.None))!;
        loaded.ShouldBe(backtest with { Parameters = loaded.Parameters, Gaps = loaded.Gaps });
        loaded.Parameters.ShouldBe(backtest.Parameters);
        loaded.Gaps.ShouldBe(backtest.Gaps);
        (await _repository.GetAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Result_is_read_back_without_losing_precision_and_a_second_insert_adds_nothing()
    {
        Backtest backtest = Backtest(Created) with { Status = BacktestStatus.Running };
        await _repository.AddAsync(backtest, CancellationToken.None);
        BacktestResult result = Result(backtest.Id, 3);

        (await _repository.SaveProgressAsync(backtest.Progress(1, null), [result], CancellationToken.None)).ShouldBeTrue();
        (await _repository.SaveProgressAsync(backtest.Progress(1, null), [result with { Metrics = result.Metrics with { Pnl = 0m } }], CancellationToken.None)).ShouldBeTrue();

        BacktestResult loaded = (await _repository.GetResultAsync(backtest.Id, 3, CancellationToken.None))!;
        loaded.ShouldBe(result with { ParameterValues = loaded.ParameterValues });
        loaded.ParameterValues.ShouldBe(result.ParameterValues);
        (await _repository.GetCalculatedCombinationsAsync(backtest.Id, CancellationToken.None)).ShouldBe([3L]);
        (await _repository.GetAsync(backtest.Id, CancellationToken.None))!.CalculatedCombinations.ShouldBe(1);
    }

    [Test]
    public async Task Progress_of_a_backtest_that_is_no_longer_running_writes_nothing()
    {
        Backtest backtest = Backtest(Created) with { Status = BacktestStatus.Cancelled };
        await _repository.AddAsync(backtest, CancellationToken.None);

        (await _repository.SaveProgressAsync(backtest with { Status = BacktestStatus.Running, CalculatedCombinations = 1 }, [Result(backtest.Id, 0)], CancellationToken.None))
            .ShouldBeFalse();

        (await _repository.GetCalculatedCombinationsAsync(backtest.Id, CancellationToken.None)).ShouldBeEmpty();
        (await _repository.GetAsync(backtest.Id, CancellationToken.None))!.Status.ShouldBe(BacktestStatus.Cancelled);
    }

    [Test]
    public async Task Update_writes_only_from_the_expected_status()
    {
        Backtest backtest = Backtest(Created);
        await _repository.AddAsync(backtest, CancellationToken.None);

        (await _repository.UpdateAsync(backtest.StartLoading(Created), BacktestStatus.Running, CancellationToken.None)).ShouldBeFalse();
        (await _repository.GetAsync(backtest.Id, CancellationToken.None))!.Status.ShouldBe(BacktestStatus.Pending);
        (await _repository.UpdateAsync(backtest.StartLoading(Created), BacktestStatus.Pending, CancellationToken.None)).ShouldBeTrue();

        (await _repository.GetAsync(backtest.Id, CancellationToken.None))!.Status.ShouldBe(BacktestStatus.LoadingCandles);
    }

    [TestCase(BacktestStatus.Pending)]
    [TestCase(BacktestStatus.LoadingCandles)]
    [TestCase(BacktestStatus.Running)]
    public async Task Status_changes_from_one_of_the_expected_statuses_with_the_time_it_finished(BacktestStatus status)
    {
        Backtest backtest = Backtest(Created) with { Status = status };
        await _repository.AddAsync(backtest, CancellationToken.None);

        bool changed = await _repository.UpdateStatusAsync(
            backtest.Id,
            [BacktestStatus.Pending, BacktestStatus.LoadingCandles, BacktestStatus.Running],
            BacktestStatus.Cancelled,
            Created.AddMinutes(3),
            CancellationToken.None);

        changed.ShouldBeTrue();
        Backtest cancelled = (await _repository.GetAsync(backtest.Id, CancellationToken.None))!;
        (cancelled.Status, cancelled.FinishedAt).ShouldBe((BacktestStatus.Cancelled, Created.AddMinutes(3)));
    }

    [Test]
    public async Task Status_of_a_backtest_in_another_status_or_of_none_does_not_change()
    {
        Backtest backtest = Backtest(Created) with { Status = BacktestStatus.Succeeded, FinishedAt = Created.AddMinutes(1) };
        await _repository.AddAsync(backtest, CancellationToken.None);

        (await _repository.UpdateStatusAsync(backtest.Id, [BacktestStatus.Running], BacktestStatus.Cancelled, Created.AddMinutes(3), CancellationToken.None))
            .ShouldBeFalse();
        (await _repository.UpdateStatusAsync(Guid.NewGuid(), [BacktestStatus.Running], BacktestStatus.Cancelled, Created.AddMinutes(3), CancellationToken.None))
            .ShouldBeFalse();

        Backtest stored = (await _repository.GetAsync(backtest.Id, CancellationToken.None))!;
        (stored.Status, stored.FinishedAt).ShouldBe((BacktestStatus.Succeeded, Created.AddMinutes(1)));
    }

    [Test]
    public async Task Lists_give_the_bot_backtests_newest_first_and_the_unfinished_ones_oldest_first()
    {
        Backtest older = Backtest(Created) with { Status = BacktestStatus.Running };
        Backtest newer = Backtest(Created.AddHours(1));
        Backtest finished = Backtest(Created.AddHours(2)) with { Status = BacktestStatus.Succeeded };
        await _repository.AddAsync(newer, CancellationToken.None);
        await _repository.AddAsync(older, CancellationToken.None);
        await _repository.AddAsync(finished, CancellationToken.None);

        (await _repository.GetByBotAsync(_bot.Id, CancellationToken.None)).Select(backtest => backtest.Id).ShouldBe([finished.Id, newer.Id, older.Id]);
        (await _repository.GetUnfinishedAsync(CancellationToken.None)).Select(backtest => backtest.Id).ShouldBe([older.Id, newer.Id]);
    }

    // Scenario "Бот с бэктестами".
    [Test]
    public async Task Deleted_bot_takes_its_backtests_and_their_results_along()
    {
        Backtest first = Backtest(Created) with { Status = BacktestStatus.Running };
        Backtest second = Backtest(Created.AddHours(1));
        await _repository.AddAsync(first, CancellationToken.None);
        await _repository.AddAsync(second, CancellationToken.None);
        await _repository.SaveProgressAsync(first.Progress(1, null), [Result(first.Id, 0)], CancellationToken.None);
        StrategyLibrary library = await _library.LoadAsync(CancellationToken.None);
        library.RemoveBot(_bot.Id);

        (await _library.SaveAsync(library.Change, library.Revision, CancellationToken.None)).ShouldBeTrue();

        (await _repository.GetAsync(first.Id, CancellationToken.None)).ShouldBeNull();
        (await _repository.GetAsync(second.Id, CancellationToken.None)).ShouldBeNull();
        (await _repository.GetResultAsync(first.Id, 0, CancellationToken.None)).ShouldBeNull();
    }

    private Backtest Backtest(DateTimeOffset createdAt)
    {
        return new Backtest(
            Guid.NewGuid(),
            _bot.Id,
            _bot.StrategyId,
            1,
            _bot.Instrument,
            _bot.Interval,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2024-02-01T00:00:00Z"),
            new Dictionary<string, ParameterSpec>
            {
                ["fast"] = ParameterSpec.Between(new NumberValue(5m), new NumberValue(15m), new NumberValue(5m)),
                ["slow"] = ParameterSpec.Of(new NumberValue(26m)),
                ["amount"] = ParameterSpec.Of(new NumberValue(100.123456789012345678m))
            },
            new SimulationSettings(1000.000000000001m, 0.02m, 0.055m, 0.0001m, LimitFillMode.Touch),
            new InstrumentRules(0.1m, 0.001m, 0.001m, 5m),
            SearchMode.Grid,
            BacktestStatus.Pending,
            null,
            null,
            null,
            [],
            0,
            0,
            0,
            3,
            null,
            0,
            createdAt,
            null,
            null);
    }

    private static BacktestResult Result(Guid backtestId, long combination)
    {
        return new BacktestResult(
            backtestId,
            combination,
            new Dictionary<string, NodeValue> { ["fast"] = new NumberValue(10m), ["every"] = new DurationValue(TimeSpan.FromHours(4)) },
            new BacktestMetrics(1000m, 1010.123456789012345678m, 10.123456789012345678m, 5m, 5.123456789012345678m, 1.0123456789012345678m, 2.5m, null,
                3, null, 7, 0.1234567890123456789m, 300m, 30m, 399.99m, 60m, 40m, 4, 2, 744),
            TimeSpan.FromMilliseconds(12.5));
    }
}