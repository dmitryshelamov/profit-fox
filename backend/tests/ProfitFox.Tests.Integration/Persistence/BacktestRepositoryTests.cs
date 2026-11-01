using Npgsql;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

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
        Backtest backtest = await RunningAsync(Backtest(Created));
        var owner = Guid.NewGuid();
        BacktestChunk chunk = (await ClaimAsync(backtest, owner, Created))!;
        BacktestResult result = Result(backtest.Id, 3);

        (await SaveAsync(chunk, owner, Created, result)).ShouldBeTrue();
        (await SaveAsync(chunk, owner, Created, result with { Metrics = result.Metrics with { Pnl = 0m } })).ShouldBeTrue();

        BacktestResult loaded = (await _repository.GetResultAsync(backtest.Id, 3, CancellationToken.None))!;
        loaded.ShouldBe(result with { ParameterValues = loaded.ParameterValues });
        loaded.ParameterValues.ShouldBe(result.ParameterValues);
        (await _repository.GetCalculatedCombinationsAsync(backtest.Id, CancellationToken.None)).ShouldBe([3L]);
        (await _repository.GetAsync(backtest.Id, CancellationToken.None))!.CalculatedCombinations.ShouldBe(1);
    }

    [Test]
    public async Task Progress_of_a_backtest_that_is_no_longer_running_writes_nothing()
    {
        Backtest backtest = await RunningAsync(Backtest(Created));
        var owner = Guid.NewGuid();
        BacktestChunk chunk = (await ClaimAsync(backtest, owner, Created))!;
        await _repository.UpdateStatusAsync(backtest.Id, [BacktestStatus.Running], BacktestStatus.Cancelled, Created, CancellationToken.None);

        (await SaveAsync(chunk, owner, Created, Result(backtest.Id, 0))).ShouldBeFalse();

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
        (await _repository.GetUnfinishedAsync(EngineVersion.Current, CancellationToken.None)).Select(backtest => backtest.Id).ShouldBe([older.Id, newer.Id]);
    }

    // Scenario "Бот с бэктестами".
    [Test]
    public async Task Deleted_bot_takes_its_backtests_and_their_results_along()
    {
        Backtest first = await RunningAsync(Backtest(Created));
        Backtest second = Backtest(Created.AddHours(1));
        await _repository.AddAsync(second, CancellationToken.None);
        var owner = Guid.NewGuid();
        await SaveAsync((await ClaimAsync(first, owner, Created))!, owner, Created, Result(first.Id, 0));
        StrategyLibrary library = await _library.LoadAsync(CancellationToken.None);
        library.RemoveBot(_bot.Id);

        (await _library.SaveAsync(library.Change, library.Revision, CancellationToken.None)).ShouldBeTrue();

        (await _repository.GetAsync(first.Id, CancellationToken.None)).ShouldBeNull();
        (await _repository.GetAsync(second.Id, CancellationToken.None)).ShouldBeNull();
        (await _repository.GetResultAsync(first.Id, 0, CancellationToken.None)).ShouldBeNull();
    }

    // Scenario "Занятый кусок".
    [Test]
    public async Task Each_worker_claims_its_own_chunk_in_order_and_none_is_left_for_a_third()
    {
        Backtest backtest = await RunningAsync(Backtest(Created), chunkSize: 2);

        BacktestChunk? first = await ClaimAsync(backtest, Guid.NewGuid(), Created);
        BacktestChunk? second = await ClaimAsync(backtest, Guid.NewGuid(), Created);
        BacktestChunk? third = await ClaimAsync(backtest, Guid.NewGuid(), Created);

        (first, second, third).ShouldBe((new BacktestChunk(backtest.Id, 0, 0, 2), new BacktestChunk(backtest.Id, 1, 2, 1), null));
    }

    [Test]
    public async Task Workers_claiming_at_once_never_take_the_same_chunk()
    {
        Backtest backtest = await RunningAsync(Backtest(Created), chunkSize: 1);

        BacktestChunk?[] claimed = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ClaimAsync(backtest, Guid.NewGuid(), Created)));

        claimed.OfType<BacktestChunk>().Select(chunk => chunk.Number).Order().ShouldBe([0, 1, 2]);
    }

    // Scenario "Аренда перехвачена".
    [Test]
    public async Task Chunk_whose_lease_expired_is_taken_over_with_a_lost_lease_and_its_first_owner_saves_nothing()
    {
        Backtest backtest = await RunningAsync(Backtest(Created));
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await ClaimAsync(backtest, a, Created);

        BacktestChunk taken = (await ClaimAsync(backtest, b, Created.AddSeconds(30)))!;

        taken.ShouldBe(new BacktestChunk(backtest.Id, 0, 0, 3, LostLeases: 1));
        (await SaveAsync(taken, a, Created.AddSeconds(31), Result(backtest.Id, 0))).ShouldBeFalse();
        (await _repository.RenewChunkLeaseAsync(taken, a, Created.AddSeconds(90), CancellationToken.None)).ShouldBeFalse();
        (await _repository.CompleteChunkAsync(taken, a, CancellationToken.None)).ShouldBeFalse();
        (await _repository.GetCalculatedCombinationsAsync(backtest.Id, CancellationToken.None)).ShouldBeEmpty();
        (await SaveAsync(taken, b, Created.AddSeconds(31), Result(backtest.Id, 0))).ShouldBeTrue();
    }

    [Test]
    public async Task Renewed_lease_keeps_the_chunk_from_other_workers()
    {
        Backtest backtest = await RunningAsync(Backtest(Created));
        var a = Guid.NewGuid();
        BacktestChunk chunk = (await ClaimAsync(backtest, a, Created))!;

        (await _repository.RenewChunkLeaseAsync(chunk, a, Created.AddSeconds(60), CancellationToken.None)).ShouldBeTrue();

        (await ClaimAsync(backtest, Guid.NewGuid(), Created.AddSeconds(31))).ShouldBeNull();
    }

    // Scenarios "Кусок без продвижения" and "Worker упал во время прогона": a saved result starts the count over.
    [Test]
    public async Task Saved_results_end_the_lost_leases_of_a_chunk()
    {
        Backtest backtest = await RunningAsync(Backtest(Created));
        await ClaimAsync(backtest, Guid.NewGuid(), Created);
        var b = Guid.NewGuid();
        BacktestChunk taken = (await ClaimAsync(backtest, b, Created.AddSeconds(30)))!;
        var c = Guid.NewGuid();
        BacktestChunk again = (await ClaimAsync(backtest, c, Created.AddSeconds(60)))!;
        await SaveAsync(again, c, Created.AddSeconds(61), Result(backtest.Id, 0));

        BacktestChunk afterSave = (await ClaimAsync(backtest, Guid.NewGuid(), Created.AddSeconds(91)))!;

        (taken.LostLeases, again.LostLeases, afterSave.LostLeases).ShouldBe((1, 2, 1));
    }

    // Scenario "Прогресс двух Worker'ов".
    [Test]
    public async Task Backtest_counts_the_results_of_every_chunk_and_sums_the_speed_of_chunks_with_a_live_lease()
    {
        Backtest backtest = await RunningAsync(Backtest(Created) with { TotalCombinations = 8 }, chunkSize: 4);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        BacktestChunk first = (await ClaimAsync(backtest, a, Created))!;
        BacktestChunk second = (await ClaimAsync(backtest, b, Created))!;

        await SaveAsync(first, a, Created.AddSeconds(1), 100m, Result(backtest.Id, 0), Result(backtest.Id, 1), Result(backtest.Id, 2));
        await SaveAsync(second, b, Created.AddSeconds(2), 200m, Result(backtest.Id, 4), Result(backtest.Id, 5), Result(backtest.Id, 6), Result(backtest.Id, 7));

        Backtest stored = (await _repository.GetAsync(backtest.Id, CancellationToken.None))!;
        (stored.CalculatedCombinations, stored.Speed).ShouldBe((7L, 300m));
    }

    // Requirement "Куски бэктеста": the backtest succeeds when its last chunk is done, and only once.
    [Test]
    public async Task Backtest_succeeds_once_its_last_chunk_is_done()
    {
        Backtest backtest = await RunningAsync(Backtest(Created), chunkSize: 2);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        BacktestChunk first = (await ClaimAsync(backtest, a, Created))!;
        BacktestChunk second = (await ClaimAsync(backtest, b, Created))!;
        await _repository.CompleteChunkAsync(first, a, CancellationToken.None);

        bool early = await _repository.SucceedIfCompleteAsync(backtest.Id, Created.AddMinutes(1), CancellationToken.None);
        await _repository.CompleteChunkAsync(second, b, CancellationToken.None);
        bool[] last = await Task.WhenAll(
            _repository.SucceedIfCompleteAsync(backtest.Id, Created.AddMinutes(2), CancellationToken.None),
            _repository.SucceedIfCompleteAsync(backtest.Id, Created.AddMinutes(2), CancellationToken.None));

        early.ShouldBeFalse();
        last.Count(succeeded => succeeded).ShouldBe(1);
        Backtest stored = (await _repository.GetAsync(backtest.Id, CancellationToken.None))!;
        (stored.Status, stored.FinishedAt).ShouldBe((BacktestStatus.Succeeded, Created.AddMinutes(2)));
        (await ClaimAsync(backtest, Guid.NewGuid(), Created.AddHours(1))).ShouldBeNull();
    }

    // Requirement "Отмена бэктеста" with several Workers.
    [Test]
    public async Task Cancelled_backtest_takes_no_more_results_from_any_worker()
    {
        Backtest backtest = await RunningAsync(Backtest(Created), chunkSize: 2);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        BacktestChunk first = (await ClaimAsync(backtest, a, Created))!;
        BacktestChunk second = (await ClaimAsync(backtest, b, Created))!;

        await _repository.UpdateStatusAsync(backtest.Id, [BacktestStatus.Running], BacktestStatus.Cancelled, Created, CancellationToken.None);

        (await SaveAsync(first, a, Created, Result(backtest.Id, 0))).ShouldBeFalse();
        (await SaveAsync(second, b, Created, Result(backtest.Id, 2))).ShouldBeFalse();
        (await _repository.GetCalculatedCombinationsAsync(backtest.Id, CancellationToken.None)).ShouldBeEmpty();
    }

    // Two Workers that take the same step of a loading backtest at once end the same way: one import job, one set of chunks.
    [Test]
    public async Task Import_and_chunks_of_a_loading_backtest_are_added_once()
    {
        Backtest loading = Backtest(Created).StartLoading(Created);
        await _repository.AddAsync(loading, CancellationToken.None);
        CandleImportJob job = CandleImportJobGenerator.Generate(CandleImportStatus.Pending);
        CandleImportJob other = CandleImportJobGenerator.Generate(CandleImportStatus.Pending);
        var jobs = new CandleImportJobRepository(_dataSource);

        bool[] imports = await Task.WhenAll(
            _repository.StartImportAsync(loading.WaitForImport(job.Id), job, CancellationToken.None),
            _repository.StartImportAsync(loading.WaitForImport(other.Id), other, CancellationToken.None));
        Backtest waiting = (await _repository.GetAsync(loading.Id, CancellationToken.None))!;
        Backtest running = waiting.StartRunning(null, []);
        bool[] runs = await Task.WhenAll(
            _repository.StartRunningAsync(running, BacktestChunk.Split(running.Id, 3, 1), CancellationToken.None),
            _repository.StartRunningAsync(running, BacktestChunk.Split(running.Id, 3, 1), CancellationToken.None));

        imports.Count(added => added).ShouldBe(1);
        (await jobs.GetAsync(waiting.ImportJobId!.Value, CancellationToken.None)).ShouldNotBeNull();
        (await jobs.GetAsync(waiting.ImportJobId == job.Id ? other.Id : job.Id, CancellationToken.None)).ShouldBeNull();
        runs.Count(started => started).ShouldBe(1);
        (await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ClaimAsync(running, Guid.NewGuid(), Created)))).Count(chunk => chunk is not null).ShouldBe(3);
    }

    [Test]
    public async Task Fail_ends_the_backtest_with_a_reason_only_from_an_expected_status()
    {
        Backtest backtest = Backtest(Created);
        await _repository.AddAsync(backtest, CancellationToken.None);

        bool fromRunning = await _repository.FailAsync(backtest.Id, [BacktestStatus.Running], "no", Created, CancellationToken.None);
        bool fromPending = await _repository.FailAsync(backtest.Id, [BacktestStatus.Pending], "Bybit is unavailable.", Created.AddMinutes(1), CancellationToken.None);

        (fromRunning, fromPending).ShouldBe((false, true));
        Backtest failed = (await _repository.GetAsync(backtest.Id, CancellationToken.None))!;
        (failed.Status, failed.Error, failed.FinishedAt).ShouldBe((BacktestStatus.Failed, "Bybit is unavailable.", Created.AddMinutes(1)));
    }

    /// <summary>Adds the backtest loading and moves it to running with chunks of <paramref name="chunkSize" /> combinations.</summary>
    private async Task<Backtest> RunningAsync(Backtest backtest, int chunkSize = 100)
    {
        Backtest loading = backtest.StartLoading(backtest.CreatedAt);
        await _repository.AddAsync(loading, CancellationToken.None);
        Backtest running = loading.StartRunning(null, []);
        await _repository.StartRunningAsync(running, BacktestChunk.Split(running.Id, running.TotalCombinations, chunkSize), CancellationToken.None);

        return running;
    }

    /// <summary>Claims a chunk under a lease of thirty seconds from <paramref name="now" />.</summary>
    private Task<BacktestChunk?> ClaimAsync(Backtest backtest, Guid owner, DateTimeOffset now)
    {
        return _repository.ClaimChunkAsync(backtest.Id, owner, now, now.AddSeconds(30), CancellationToken.None);
    }

    private Task<bool> SaveAsync(BacktestChunk chunk, Guid owner, DateTimeOffset now, params BacktestResult[] results)
    {
        return SaveAsync(chunk, owner, now, null, results);
    }

    private Task<bool> SaveAsync(BacktestChunk chunk, Guid owner, DateTimeOffset now, decimal? speed, params BacktestResult[] results)
    {
        return _repository.SaveChunkProgressAsync(chunk, owner, results, speed, now, CancellationToken.None);
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
            createdAt,
            null,
            null,
            EngineVersion.Current);
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