using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Fixtures.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner.Ideas;

/// <summary>
///     Requirement "Прогон по истории" and decisions 6 and 7 on the impulse set of <see cref="IdeaFixtures.Setup" />: the pair's history starts
///     on 2024-03-01, its minutes are stored up to the end of the day. The replay runs by the thresholds of the live ideas.
/// </summary>
public class IdeasReplayTests
{
    private static readonly Instrument Wif = new Instrument(MarketCategory.Linear, "WIFUSDT");
    private static readonly DateTimeOffset March1 = DateTimeOffset.Parse("2024-03-01T00:00:00Z");
    private static readonly FeeRates Fees = new FeeRates(0.02m, 0.055m);
    private const decimal Tick = 0.01m;
    private static readonly IdeasSettings Settings = new IdeasSettings();

    private ICandleRepository _candles = null!;
    private IIdeaCaseRepository _cases = null!;
    private IIdeaReplayRepository _cursors = null!;
    private IIdeaVolatilityRepository _volatility = null!;
    private IIdeaBucketRepository _buckets = null!;
    private IIdeaCalibrationRepository _calibrations = null!;
    private ScannerMarket _market = null!;
    private ScannerPace _pace = null!;
    private FakeTimeProvider _time = null!;
    private List<IdeaCase> _written = null!;
    private Dictionary<Instrument, IdeaReplayCursor> _stored = null!;
    private IReadOnlyList<IdeaCandle> _minutes = null!;

    [SetUp]
    public void SetUp()
    {
        _candles = Substitute.For<ICandleRepository>();
        _cases = Substitute.For<IIdeaCaseRepository>();
        _cursors = Substitute.For<IIdeaReplayRepository>();
        _volatility = Substitute.For<IIdeaVolatilityRepository>();
        _buckets = Substitute.For<IIdeaBucketRepository>();
        _calibrations = Substitute.For<IIdeaCalibrationRepository>();
        _market = new ScannerMarket();
        _pace = new ScannerPace();
        _time = new FakeTimeProvider(March1.AddDays(1).AddMinutes(5));
        _written = [];
        _minutes = IdeaFixtures.Setup("impulse");
        _stored = [];
        _cursors.GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => new Dictionary<Instrument, IdeaReplayCursor>(_stored));
        _cursors.SetAsync(Arg.Any<Instrument>(), Arg.Any<int>(), Arg.Any<IdeaReplayCursor>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _stored[call.ArgAt<Instrument>(0)] = call.ArgAt<IdeaReplayCursor>(2);

            return Task.CompletedTask;
        });
        _cases.AddAsync(Arg.Any<IReadOnlyCollection<IdeaCase>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _written.AddRange(call.Arg<IReadOnlyCollection<IdeaCase>>()!);

            return Task.CompletedTask;
        });
        _candles.GetAsync(Wif, Interval.OneMinute, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
            (IReadOnlyList<Candle>)[.. _minutes.Where(minute => minute.OpenTime >= call.ArgAt<DateTimeOffset>(2) && minute.OpenTime < call.ArgAt<DateTimeOffset>(3)).Select(Candle)]);
        _candles.GetAggregatedAsync(Wif, Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(call =>
            IdeaCandles.FromMinutes(call.ArgAt<Interval>(1), _minutes, call.ArgAt<DateTimeOffset>(2), call.ArgAt<DateTimeOffset>(3)));
        Keep(March1.AddDays(1));
    }

    // Task 5.1: "прогон дня = живой цикл на тех же минутах дают одинаковые случаи".
    [Test]
    public async Task Replay_of_a_day_gives_the_cases_the_live_engine_gives_on_the_same_minutes()
    {
        (await Replay().StepAsync(CancellationToken.None)).ShouldBeTrue();

        _written.ShouldNotBeEmpty();
        Shape(_written).ShouldBe(Shape(Live()));
        await _cursors.Received(1).SetAsync(Wif, IdeasEngineVersion.Current, Until(March1.AddDays(1)), Arg.Any<CancellationToken>());
    }

    // Task 5.1: "новый день не трогает закрытые случаи старых дней": the next turn goes on from the cursor and its open ideas.
    [Test]
    public async Task New_day_leaves_the_cases_closed_before_it_as_they_are()
    {
        IdeasReplay replay = Replay();
        await replay.StepAsync(CancellationToken.None);
        int first = _written.Count;
        Keep(March1.AddDays(2));
        _time.Advance(TimeSpan.FromDays(1));

        (await replay.StepAsync(CancellationToken.None)).ShouldBeTrue();

        _written.Count.ShouldBe(first);
        await _candles.Received(2).GetAsync(Wif, Interval.OneMinute, March1, March1.AddDays(1), 1440, Arg.Any<CancellationToken>());
        await _cursors.Received(1).SetAsync(Wif, IdeasEngineVersion.Current, Until(March1.AddDays(1)), Arg.Any<CancellationToken>());
        await _cursors.Received(1).SetAsync(Wif, IdeasEngineVersion.Current, Until(March1.AddDays(2)), Arg.Any<CancellationToken>());
        (await replay.StepAsync(CancellationToken.None)).ShouldBeFalse();
    }

    // History may start before the first trade: the days before it pass, and the turn still moves the cursor over them.
    [Test]
    public async Task Days_before_the_first_minute_of_a_pair_pass_and_move_the_cursor()
    {
        _market.Keep([new ScannerPair(Wif, true, HistoryFrom: March1.AddDays(-3), CoveredUntil: March1.AddDays(1), TickSize: Tick)]);
        IdeasReplay replay = Replay();

        (await replay.StepAsync(CancellationToken.None)).ShouldBeTrue();

        Shape(_written).ShouldBe(Shape(Live()));
        await _cursors.Received(1).SetAsync(Wif, IdeasEngineVersion.Current, Until(March1.AddDays(-2)), Arg.Any<CancellationToken>());
        (await replay.StepAsync(CancellationToken.None)).ShouldBeFalse();
    }

    // Pairs take turns: the one furthest behind goes first, a pair never replayed before all others.
    [Test]
    public async Task Pair_furthest_behind_takes_the_next_turn()
    {
        var sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
        _stored[Wif] = new IdeaReplayCursor(March1, []);
        _market.Keep([new ScannerPair(sol, true, HistoryFrom: March1, CoveredUntil: March1.AddDays(1), TickSize: Tick)]);

        await Replay().StepAsync(CancellationToken.None);

        await _cursors.Received(1).SetAsync(sol, IdeasEngineVersion.Current, Until(March1.AddDays(1)), Arg.Any<CancellationToken>());
        await _cursors.DidNotReceive().SetAsync(Wif, Arg.Any<int>(), Arg.Any<IdeaReplayCursor>(), Arg.Any<CancellationToken>());
    }

    // Fix after review: a cursor older than the history window (the pair left trading and came back, or the replay stood still) starts the
    // pair over from the start of its history; the ideas it kept open are dropped.
    [Test]
    public async Task Cursor_older_than_the_history_starts_the_pair_over()
    {
        Idea stale = IdeaGenerator.Generate(March1.AddDays(-40), IdeaStatus.Active, Wif, statusAt: March1.AddDays(-40));
        _stored[Wif] = new IdeaReplayCursor(March1.AddDays(-30), [stale]);

        await Replay().StepAsync(CancellationToken.None);

        Shape(_written).ShouldBe(Shape(Live()));
        await _candles.DidNotReceive().GetAsync(Wif, Interval.OneMinute, March1.AddDays(-30), March1.AddDays(-29), 1440, Arg.Any<CancellationToken>());
        _stored[Wif].Open.ShouldNotContain(idea => idea.Id == stale.Id);
    }

    // A pair whose history is not loaded whole waits for it.
    [Test]
    public async Task Pair_without_its_whole_history_is_not_replayed()
    {
        _market.Keep([new ScannerPair(Wif, true, CoveredUntil: March1.AddDays(1), TickSize: Tick)]);

        (await Replay().StepAsync(CancellationToken.None)).ShouldBeFalse();
    }

    // A pair whose price step the list of instruments has not told yet waits for it: a day replayed without ideas would never be replayed again.
    [Test]
    public async Task Pair_without_its_price_step_is_not_replayed()
    {
        _market.Keep([new ScannerPair(Wif, true, HistoryFrom: March1, CoveredUntil: March1.AddDays(1))]);

        (await Replay().StepAsync(CancellationToken.None)).ShouldBeFalse();
    }

    // Decision 1: the replay waits between days while the minute cycle is late.
    [Test]
    public async Task Replay_waits_while_the_minute_cycle_is_late()
    {
        _pace.Snapshot(_time.GetUtcNow(), _time.GetUtcNow().AddSeconds(3));
        Task<bool> step = Replay().StepAsync(CancellationToken.None);
        await Task.Delay(50);

        await _candles.DidNotReceive().GetAsync(Wif, Interval.OneMinute, March1, March1.AddDays(1), 1440, Arg.Any<CancellationToken>());

        _pace.Snapshot(_time.GetUtcNow(), _time.GetUtcNow());
        _time.Advance(TimeSpan.FromSeconds(10));
        (await step).ShouldBeTrue();
        _written.ShouldNotBeEmpty();
    }

    // Scenario "Повторный прогон после смены правил": the cases and cursors of other versions go, so a new version replays from the start.
    [Test]
    public async Task Clean_up_removes_the_cases_and_cursors_of_other_versions()
    {
        await Replay(2).CleanUpAsync(CancellationToken.None);

        await _cases.Received(1).DeleteOtherVersionsAsync(2, Arg.Any<CancellationToken>());
        await _cursors.Received(1).DeleteOtherVersionsAsync(2, Arg.Any<CancellationToken>());
    }

    // Task 5.2: the buckets of the version over 90 and over the last 30 days.
    [Test]
    public async Task Buckets_are_computed_over_90_and_30_days_by_the_thresholds_of_now()
    {
        var key = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.OneHour, IdeaSide.Long, MarketCategory.Linear);
        DateTimeOffset now = _time.GetUtcNow();
        _cases.CountAsync(2, now.AddDays(-90), Arg.Any<CancellationToken>())
            .Returns([new IdeaBucketCounts(key, 180, 200, 20, 48m, 580.32m)]);
        _cases.CountAsync(2, now.AddDays(-30), Arg.Any<CancellationToken>())
            .Returns([new IdeaBucketCounts(key, 6, 5, 1, 2.4m, 20m)]);

        await Replay(2).RecomputeBucketsAsync(CancellationToken.None);

        await _cases.Received(2).CountAsync(2, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _buckets.Received(1).ReplaceAsync(
            2,
            Arg.Is<IReadOnlyCollection<IdeaBucket>>(buckets => buckets!.Count == 2
                && buckets.Single(bucket => bucket.Days == 90).Statistics == BucketStatisticsCalculator.Compute(180, 200, 20, 48m, 580.32m)
                && buckets.Single(bucket => bucket.Days == 30).Statistics == BucketStatisticsCalculator.Compute(6, 5, 1, 2.4m, 20m)),
            Arg.Any<CancellationToken>());
    }

    // Task 5.6: the calibration follows the buckets, on the cases since 180 days before the time of the clock.
    [Test]
    public async Task Calibration_is_recomputed_after_the_buckets_at_the_time_of_the_clock()
    {
        DateTimeOffset now = _time.GetUtcNow();
        var key = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.OneHour, IdeaSide.Long, MarketCategory.Linear);
        _cases.GetOutcomesAsync(2, now.AddDays(-180), Arg.Any<CancellationToken>()).Returns([new IdeaOutcome(key, now.AddDays(-1), now.AddHours(-1), IdeaStatus.Target, 2m)]);

        await Replay(2).RecomputeBucketsAsync(CancellationToken.None);

        Received.InOrder(() =>
        {
            _buckets.ReplaceAsync(2, Arg.Any<IReadOnlyCollection<IdeaBucket>>(), Arg.Any<CancellationToken>());
            _calibrations.ReplaceAsync(
                2,
                Arg.Is<IReadOnlyCollection<CalibrationReport>>(reports => reports!.Count == 6
                    && reports.All(report => report.ComputedAt == now)
                    && reports.Single(report => report.Days == 30 && report.Category == MarketCategory.Linear).Unchecked == 1),
                Arg.Any<CancellationToken>());
        });
    }

    // Decision 5 and scenario "Перцентили после прогона": the percentiles of a day come from the candles before it; after the turn the last
    // 10th and 90th go to storage for the live ideas, of the same ratios.
    [Test]
    public async Task Turn_stores_the_percentiles_of_volatility_of_the_days_it_replayed()
    {
        _minutes = Waves(March1, March1.AddDays(2));
        Keep(March1.AddDays(2));
        _time.Advance(TimeSpan.FromDays(1));

        await Replay().StepAsync(CancellationToken.None);

        IReadOnlyList<decimal> ratios = Volatility.Ratios(IdeaCandles.FromMinutes(Interval.FiveMinutes, _minutes, March1, March1.AddDays(2)));
        decimal expected = Volatility.Percentile(ratios, 0.9m)!.Value;
        decimal low = Volatility.Percentile(ratios, 0.1m)!.Value;
        await _volatility.Received(1).SaveAsync(
            Arg.Is<IReadOnlyCollection<IdeaVolatility>>(saved => saved!.Single(item => item.Interval == Interval.FiveMinutes).Percentile == expected
                && saved!.Single(item => item.Interval == Interval.FiveMinutes).Low == low
                && saved!.Any(item => item.Interval == Interval.FifteenMinutes) && !saved!.Any(item => item.Interval == Interval.OneHour)),
            Arg.Any<CancellationToken>());
    }

    // Fix after review: the cases do not depend on how the days fall into turns. Four days of waves replayed in one turn and day by day,
    // each day by a replay of its own over the stored cursor; the third day starts without trades and opens 2 % up. The waves start 4 hours
    // before the history, so an idea of 15 minutes fired late on the second day closes on the third. Every third stretch of 211 minutes, the
    // steepest, trades three times the volume, so the candle setups have breakouts beside squeezes and RSI back from its zones.
    [Test]
    public async Task Replay_in_one_turn_and_day_by_day_gives_the_same_cases()
    {
        DateTimeOffset end = March1.AddDays(4);
        DateTimeOffset gap = March1.AddDays(2);
        _minutes = [.. Waves(March1.AddHours(-4), end).Where(minute => minute.OpenTime < gap || minute.OpenTime >= gap.AddMinutes(10))
            .Select(minute => minute.OpenTime >= gap ? minute with { Open = minute.Open * 1.02m, High = minute.High * 1.02m, Low = minute.Low * 1.02m, Close = minute.Close * 1.02m } : minute)
            .Select((minute, index) => index / 211 % 3 == 2 ? minute with { Volume = 300m } : minute)];
        _volatility.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _time.Advance(TimeSpan.FromDays(3));
        Keep(end);
        await Replay().StepAsync(CancellationToken.None);
        string[] whole = [.. Shape(_written)];
        IReadOnlyList<Idea> openAtTheEnd = _stored[Wif].Open;
        _written.Clear();
        _stored.Clear();

        for (DateTimeOffset until = March1.AddDays(1); until <= end; until = until.AddDays(1))
        {
            Keep(until);
            await Replay().StepAsync(CancellationToken.None);
        }

        whole.ShouldNotBeEmpty();
        new[] { IdeaSetup.Breakout, IdeaSetup.Squeeze, IdeaSetup.RsiExtreme }.ShouldAllBe(setup => _written.Any(item => item.Idea.Setup == setup));
        Shape(_written).ShouldBe(whole);
        _stored[Wif].Open.Select(idea => idea with { Id = Guid.Empty }).ShouldBe(openAtTheEnd.Select(idea => idea with { Id = Guid.Empty }));
        _written.ShouldContain(item => item.Idea.Track.FiredAt < Interval.OneDay.CandleOpen(item.Idea.Track.StatusAt), "A case open across a cursor");
    }

    // Fix after review: a window of a setup longer than a day (7 candles of 4 hours) is warmed up whole: the minutes before the cursor are
    // read over as many days as it spans, and one turn and day by day still give the same cases.
    [Test]
    public async Task Window_of_a_setup_longer_than_a_day_is_warmed_up_whole()
    {
        var settings = new IdeasSettings { ByInterval = new Dictionary<string, ThresholdOverride> { ["240"] = new ThresholdOverride { Candles = 7 } } };
        DateTimeOffset end = March1.AddDays(4);
        _minutes = Waves(March1.AddDays(-2), end);
        _time.Advance(TimeSpan.FromDays(3));
        Keep(end);
        await Replay(settings: settings).StepAsync(CancellationToken.None);
        string[] whole = [.. Shape(_written)];
        _written.Clear();
        _stored.Clear();
        _candles.ClearReceivedCalls();

        for (DateTimeOffset until = March1.AddDays(1); until <= end; until = until.AddDays(1))
        {
            Keep(until);
            await Replay(settings: settings).StepAsync(CancellationToken.None);
        }

        Shape(_written).ShouldBe(whole);
        await _candles.Received(1).GetAsync(Wif, Interval.OneMinute, March1.AddDays(1), March1.AddDays(3), 2880, Arg.Any<CancellationToken>());
    }

    private static IdeaReplayCursor Until(DateTimeOffset replayedUntil)
    {
        return Arg.Is<IdeaReplayCursor>(cursor => cursor!.ReplayedUntil == replayedUntil);
    }

    private void Keep(DateTimeOffset coveredUntil)
    {
        _market.Keep([new ScannerPair(Wif, true, HistoryFrom: March1, CoveredUntil: coveredUntil, TickSize: Tick)]);
    }

    private IdeasReplay Replay(int version = IdeasEngineVersion.Current, IdeasSettings? settings = null)
    {
        var backtests = new BacktestSettings { DefaultFees = new Dictionary<string, FeeRateSettings> { ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m } } };

        return new IdeasReplay(
            _candles,
            _cases,
            _cursors,
            _volatility,
            _buckets,
            new IdeaCalibration(_cases, _calibrations, new IdeasEngineVersion(version)),
            _market,
            settings ?? Settings,
            backtests,
            new ScannerSettings(),
            new IdeasEngineVersion(version),
            _pace,
            _time);
    }

    // The live engine warmed up the same way at the start of the day and fed the same minutes.
    private IReadOnlyList<IdeaCase> Live()
    {
        var engine = new IdeaEngine(Settings.Rules(), IdeasEngineVersion.Current);
        var closed = IdeasSettings.Intervals.ToDictionary(
            interval => interval,
            interval => (IReadOnlyList<IdeaCandle>)[.. IdeaCandles.FromMinutes(interval, _minutes, March1.AddDays(-100), March1).TakeLast(220)]);
        IdeaCandle[] before = [.. _minutes.Where(minute => minute.OpenTime < March1)];
        IReadOnlyList<IdeaCandle> warm = IdeaCandles.Minutes(before, before[0].OpenTime, March1, before[0].Open);
        engine.Warm(Wif, closed, warm, [], IdeaInputs.None);

        return
        [
            .. IdeaCandles.Minutes(_minutes, March1, March1.AddDays(1), warm[^1].Close)
                .SelectMany(minute => engine.Step(Wif, minute, IdeaInputs.None with { Tick = Tick }, true).Changed)
                .Where(idea => !idea.IsOpen && idea.Track.EnteredAt is not null)
                .Select(idea => new IdeaCase(idea, CaseResult.R(idea.Track, Fees)!.Value))
        ];
    }

    private static IEnumerable<string> Shape(IEnumerable<IdeaCase> cases)
    {
        return cases.Select(item => $"{item.Idea with { Id = Guid.Empty }} {item.Result}").Order();
    }

    // Minutes that swing up and down by a few percent over the hours, for ratios of volatility that vary.
    private static IReadOnlyList<IdeaCandle> Waves(DateTimeOffset from, DateTimeOffset to)
    {
        var minutes = new List<IdeaCandle>();
        decimal price = 100m;

        for (DateTimeOffset time = from; time < to; time = time.AddMinutes(1))
        {
            int minute = (int)(time - from).TotalMinutes;
            decimal step = (minute / 37 % 2 == 0 ? 0.03m : -0.03m) * (1 + (minute / 211 % 3));
            minutes.Add(new IdeaCandle(time, price, price + Math.Abs(step) + 0.01m, price - 0.01m, price + step, 100m));
            price += step;
        }

        return minutes;
    }

    private static Candle Candle(IdeaCandle minute)
    {
        return new Candle(minute.OpenTime, new Price(minute.Open), new Price(minute.High), new Price(minute.Low), new Price(minute.Close), new Volume(1m), new Volume(minute.Volume));
    }
}