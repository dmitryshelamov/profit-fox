using System.Collections;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Backtests.Commands.RunNextBacktestWork;
using ProfitFox.Application.Strategies;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Backtests;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;


namespace ProfitFox.Tests.Unit.Application.Backtests;

/// <summary>A Worker reads the candles and the strategy version of a backtest once for all the chunks it computes in a row.</summary>
public class RunNextBacktestWorkTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");

    private IBacktestRepository _backtests = null!;
    private IStrategyLibraryRepository _library = null!;
    private ICandleRepository _candles = null!;
    private ISeriesCoverageRepository _coverage = null!;
    private FakeTimeProvider _time = null!;
    private RunNextBacktestWorkCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _backtests = Substitute.For<IBacktestRepository>();
        _library = Substitute.For<IStrategyLibraryRepository>();
        _candles = Substitute.For<ICandleRepository>();
        _coverage = Substitute.For<ISeriesCoverageRepository>();
        _time = new FakeTimeProvider(Now);
        _backtests.GetCalculatedCombinationsAsync(Arg.Any<BacktestChunk>(), Arg.Any<CancellationToken>()).Returns([]);
        _backtests.SaveChunkProgressAsync(
                Arg.Any<BacktestChunk>(),
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<BacktestResult>>(),
                Arg.Any<decimal?>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        _backtests.CompleteChunkAsync(Arg.Any<BacktestChunk>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _candles.GetAsync(Arg.Any<Instrument>(), Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _handler = new RunNextBacktestWorkCommandHandler(
            _backtests,
            _library,
            _candles,
            Substitute.For<ICandleImportJobRepository>(),
            _coverage,
            new BacktestInputCache(),
            new BacktestSettings { MaxParallelism = 1 },
            new WorkerSettings(),
            new WorkerIdentity(Guid.NewGuid()),
            new EngineVersion(EngineVersion.Current),
            _time);
    }

    // After the review: on a market with sessions the nights inside the coverage are not gaps, so a covered period needs no import, reports
    // no gaps and is loaded whole.
    [Test]
    public async Task Covered_moex_period_with_nights_runs_without_an_import_and_without_gaps()
    {
        var monday = DateTimeOffset.Parse("2024-03-04T00:00:00Z");
        Backtest pending = MoexPending(monday, monday.AddDays(2));
        List<DateTimeOffset> stored = [.. Enumerable.Range(0, 2).SelectMany(day => Enumerable.Range(7, 14).Select(hour => monday.AddDays(day).AddHours(hour)))];
        _candles.GetOpenTimesAsync(pending.Instrument, pending.Interval, pending.From, pending.To, Arg.Any<CancellationToken>()).Returns(stored);
        _coverage.GetAsync(pending.Instrument, pending.Interval, Arg.Any<CancellationToken>()).Returns(new SeriesCoverage(monday, monday.AddDays(2)));

        await _handler.Handle(new RunNextBacktestWorkCommand(), CancellationToken.None);

        await _backtests.DidNotReceiveWithAnyArgs().StartImportAsync(default!, default!, default);
        await _backtests.Received(1).StartRunningAsync(
            Arg.Is<Backtest>(running => running.Gaps.Count == 0 && running.LoadedCandles == 28 && running.ExpectedCandles == 28),
            Arg.Any<IReadOnlyList<BacktestChunk>>(),
            Arg.Any<CancellationToken>());
    }

    // The candles expected are the stored ones and the slots of the part outside the coverage, so the progress reaches 100 % once the import is done.
    [Test]
    public async Task Moex_period_past_the_coverage_imports_and_expects_the_stored_candles_and_the_uncovered_slots()
    {
        var monday = DateTimeOffset.Parse("2024-03-04T00:00:00Z");
        Backtest pending = MoexPending(monday, monday.AddDays(2));
        List<DateTimeOffset> stored = [.. Enumerable.Range(7, 14).Select(hour => monday.AddHours(hour))];
        _candles.GetOpenTimesAsync(pending.Instrument, pending.Interval, pending.From, pending.To, Arg.Any<CancellationToken>()).Returns(stored);
        _coverage.GetAsync(pending.Instrument, pending.Interval, Arg.Any<CancellationToken>()).Returns(new SeriesCoverage(monday, monday.AddDays(1)));

        await _handler.Handle(new RunNextBacktestWorkCommand(), CancellationToken.None);

        await _backtests.Received(1).StartImportAsync(
            Arg.Is<Backtest>(loading => loading.LoadedCandles == 14 && loading.ExpectedCandles == 14 + 24),
            Arg.Is<CandleImportJob>(job => job.From == pending.From && job.To == pending.To),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Bybit_backtest_reads_no_coverage()
    {
        var monday = DateTimeOffset.Parse("2024-03-04T00:00:00Z");
        Backtest pending = MoexPending(monday, monday.AddDays(1)) with { Instrument = new Instrument(MarketCategory.Linear, "BTCUSDT") };
        Unfinished(pending);
        _candles.GetOpenTimesAsync(pending.Instrument, pending.Interval, pending.From, pending.To, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 24).Select(hour => monday.AddHours(hour)).ToList());

        await _handler.Handle(new RunNextBacktestWorkCommand(), CancellationToken.None);

        _coverage.ReceivedCalls().ShouldBeEmpty();
        await _backtests.Received(1).StartRunningAsync(
            Arg.Is<Backtest>(running => running.Gaps.Count == 0 && running.LoadedCandles == 24 && running.ExpectedCandles == 24),
            Arg.Any<IReadOnlyList<BacktestChunk>>(),
            Arg.Any<CancellationToken>());
    }

    // Requirement "Куски бэктеста".
    [Test]
    public async Task Second_chunk_of_the_same_backtest_does_not_read_its_candles_again()
    {
        Backtest backtest = Running();
        Unfinished(backtest, backtest);
        Chunks(backtest, new BacktestChunk(backtest.Id, 0, 0, 1), new BacktestChunk(backtest.Id, 1, 1, 1));

        await _handler.Handle(new RunNextBacktestWorkCommand(), CancellationToken.None);
        await _handler.Handle(new RunNextBacktestWorkCommand(), CancellationToken.None);

        await _candles.Received(1).GetAsync(backtest.Instrument, backtest.Interval, backtest.From, backtest.To, int.MaxValue, Arg.Any<CancellationToken>());
        await _library.Received(1).GetVersionAsync(backtest.StrategyId, backtest.StrategyVersion, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Chunk_of_another_backtest_reads_the_candles_of_that_backtest()
    {
        Backtest first = Running();
        Backtest second = Running();
        Unfinished(first, second);
        Chunks(first, new BacktestChunk(first.Id, 0, 0, 1));
        Chunks(second, new BacktestChunk(second.Id, 0, 0, 1));

        await _handler.Handle(new RunNextBacktestWorkCommand(), CancellationToken.None);
        await _handler.Handle(new RunNextBacktestWorkCommand(), CancellationToken.None);

        await _candles.Received(1).GetAsync(first.Instrument, first.Interval, first.From, first.To, int.MaxValue, Arg.Any<CancellationToken>());
        await _candles.Received(1).GetAsync(second.Instrument, second.Interval, second.From, second.To, int.MaxValue, Arg.Any<CancellationToken>());
    }

    // Requirement "Аренда работы": a Worker whose lease was taken over neither saves nor completes the chunk.
    [Test]
    public async Task Chunk_stops_without_completing_when_its_lease_cannot_be_renewed()
    {
        Backtest backtest = Running();
        Unfinished(backtest);
        Chunks(backtest, new BacktestChunk(backtest.Id, 0, 0, 1));
        var calculated = new TaskCompletionSource<IReadOnlyList<long>>();
        _backtests.GetCalculatedCombinationsAsync(Arg.Any<BacktestChunk>(), Arg.Any<CancellationToken>()).Returns(_ => calculated.Task);
        _backtests.RenewChunkLeaseAsync(Arg.Any<BacktestChunk>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(false);

        Task<bool> run = _handler.Handle(new RunNextBacktestWorkCommand(), CancellationToken.None);

        while (!_backtests.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IBacktestRepository.RenewChunkLeaseAsync)))
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5);
        }

        calculated.SetResult([]);
        (await run).ShouldBeTrue();
        await _backtests.DidNotReceiveWithAnyArgs().SaveChunkProgressAsync(default!, default, default!, default, default, default);
        await _backtests.DidNotReceiveWithAnyArgs().CompleteChunkAsync(default!, default, default);
    }

    // Combinations already started run to their end: the Worker waits for them before it takes the next chunk on the same threads.
    [Test]
    public async Task Worker_that_lost_its_chunk_waits_for_the_combinations_it_started()
    {
        Backtest backtest = Running();
        Unfinished(backtest);
        Chunks(backtest, new BacktestChunk(backtest.Id, 0, 0, 1));
        var replay = new BlockingCandles();
        _candles.GetAsync(Arg.Any<Instrument>(), Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(replay);
        _backtests.RenewChunkLeaseAsync(Arg.Any<BacktestChunk>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(false);

        Task<bool> run = _handler.Handle(new RunNextBacktestWorkCommand(), CancellationToken.None);
        await replay.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        while (!_backtests.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IBacktestRepository.RenewChunkLeaseAsync)))
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5);
        }

        await Task.Delay(200);
        bool returnedWhileComputing = run.IsCompleted;
        replay.Release.Set();

        returnedWhileComputing.ShouldBeFalse();
        (await run.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
    }

    /// <summary>A running backtest of one combination whose strategy version is the reference Ladder.</summary>
    private Backtest Running()
    {
        Backtest backtest = BacktestGenerator.Generate(BacktestStatus.Running) with { TotalCombinations = 1 };
        var snapshot = StrategySnapshot.Of(StrategyJson.ParseStrategy(StrategyFixtures.Element("ladder")), BuiltInComposites.Catalog);
        _library.GetVersionAsync(backtest.StrategyId, backtest.StrategyVersion, Arg.Any<CancellationToken>())
            .Returns(new StrategyVersion(backtest.StrategyId, backtest.StrategyVersion, snapshot, Now));

        return backtest;
    }

    /// <summary>A pending hourly backtest of SBER on moex over the period, found unfinished by every call.</summary>
    private Backtest MoexPending(DateTimeOffset from, DateTimeOffset to)
    {
        Backtest pending = BacktestGenerator.Generate(BacktestStatus.Pending) with
        {
            Instrument = new Instrument(MarketCategory.Moex, "SBER"),
            Interval = Interval.OneHour,
            From = from,
            To = to,
            TotalCombinations = 1
        };
        Unfinished(pending);

        return pending;
    }

    /// <summary>Every call finds the given backtests unfinished, oldest first.</summary>
    private void Unfinished(params Backtest[] backtests)
    {
        _backtests.GetUnfinishedAsync(EngineVersion.Current, Arg.Any<CancellationToken>()).Returns(backtests);
    }

    /// <summary>The backtest's claims lease the given chunks one after another, and then none.</summary>
    private void Chunks(Backtest backtest, params BacktestChunk[] chunks)
    {
        _backtests.ClaimChunkAsync(backtest.Id, Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(chunks[0], [.. chunks.Skip(1).Cast<BacktestChunk?>(), null]);
    }

    /// <summary>One candle whose replay waits until the test releases it, as a long combination does.</summary>
    private sealed class BlockingCandles : IReadOnlyList<Candle>
    {
        private readonly Candle _candle = CandleGenerator.Generate(Now);

        public TaskCompletionSource Started { get; } = new TaskCompletionSource();

        public ManualResetEventSlim Release { get; } = new ManualResetEventSlim();

        public int Count => 1;

        public Candle this[int index]
        {
            get
            {
                Started.TrySetResult();
                Release.Wait(TimeSpan.FromSeconds(10));

                return _candle;
            }
        }

        public IEnumerator<Candle> GetEnumerator()
        {
            yield return this[0];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}