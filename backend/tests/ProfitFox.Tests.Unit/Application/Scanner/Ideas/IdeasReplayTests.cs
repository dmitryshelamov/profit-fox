using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Ideas;
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

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner.Ideas;

/// <summary>
///     Requirement "Прогон по истории" and decisions 6 and 7 on the impulse set of <see cref="IdeaFixtures.Setup" />: the pair's history starts
///     on 2024-03-01, its minutes are stored up to the end of the day. The floors equal the thresholds, so the replay and the live ideas run by
///     the same rules.
/// </summary>
public class IdeasReplayTests
{
    private static readonly Instrument Wif = new Instrument(MarketCategory.Linear, "WIFUSDT");
    private static readonly DateTimeOffset March1 = DateTimeOffset.Parse("2024-03-01T00:00:00Z");
    private static readonly FeeRates Fees = new FeeRates(0.02m, 0.055m);
    private static readonly IdeasSettings Settings = new IdeasSettings { Floors = new ThresholdSettings() };

    private ICandleRepository _candles = null!;
    private IIdeaCaseRepository _cases = null!;
    private IIdeaReplayRepository _cursors = null!;
    private IIdeaVolatilityRepository _volatility = null!;
    private IIdeaBucketRepository _buckets = null!;
    private ScannerMarket _market = null!;
    private ScannerPace _pace = null!;
    private FakeTimeProvider _time = null!;
    private List<IdeaCase> _written = null!;
    private IReadOnlyList<IdeaCandle> _minutes = null!;

    [SetUp]
    public void SetUp()
    {
        _candles = Substitute.For<ICandleRepository>();
        _cases = Substitute.For<IIdeaCaseRepository>();
        _cursors = Substitute.For<IIdeaReplayRepository>();
        _volatility = Substitute.For<IIdeaVolatilityRepository>();
        _buckets = Substitute.For<IIdeaBucketRepository>();
        _market = new ScannerMarket();
        _pace = new ScannerPace();
        _time = new FakeTimeProvider(March1.AddDays(1).AddMinutes(5));
        _written = [];
        _minutes = IdeaFixtures.Setup("impulse");
        _cursors.GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Instrument, DateTimeOffset>());
        _cases.AddAsync(Arg.Any<IReadOnlyCollection<IdeaCase>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _written.AddRange(call.Arg<IReadOnlyCollection<IdeaCase>>());

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
        await _cursors.Received(1).SetAsync(Wif, IdeasEngineVersion.Current, March1.AddDays(1), Arg.Any<CancellationToken>());
    }

    // Task 5.1: "новый день не трогает закрытые случаи старых дней"; the turn starts again a horizon before the cursor.
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
        await _cursors.Received(1).SetAsync(Wif, IdeasEngineVersion.Current, March1.AddDays(1), Arg.Any<CancellationToken>());
        await _cursors.Received(1).SetAsync(Wif, IdeasEngineVersion.Current, March1.AddDays(2), Arg.Any<CancellationToken>());
        (await replay.StepAsync(CancellationToken.None)).ShouldBeFalse();
    }

    // History may start before the first trade: the days before it pass, and the turn still moves the cursor over them.
    [Test]
    public async Task Days_before_the_first_minute_of_a_pair_pass_and_move_the_cursor()
    {
        _market.Keep([new ScannerPair(Wif, true, HistoryFrom: March1.AddDays(-3), CoveredUntil: March1.AddDays(1))]);
        IdeasReplay replay = Replay();

        (await replay.StepAsync(CancellationToken.None)).ShouldBeTrue();

        Shape(_written).ShouldBe(Shape(Live()));
        await _cursors.Received(1).SetAsync(Wif, IdeasEngineVersion.Current, March1.AddDays(-2), Arg.Any<CancellationToken>());
        (await replay.StepAsync(CancellationToken.None)).ShouldBeFalse();
    }

    // Pairs take turns: the one furthest behind goes first, a pair never replayed before all others.
    [Test]
    public async Task Pair_furthest_behind_takes_the_next_turn()
    {
        var sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
        _cursors.GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Instrument, DateTimeOffset> { [Wif] = March1 });
        _market.Keep([new ScannerPair(sol, true, HistoryFrom: March1, CoveredUntil: March1.AddDays(1))]);

        await Replay().StepAsync(CancellationToken.None);

        await _cursors.Received(1).SetAsync(sol, IdeasEngineVersion.Current, March1.AddDays(1), Arg.Any<CancellationToken>());
        await _cursors.DidNotReceive().SetAsync(Wif, Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // A pair whose history is not loaded whole waits for it.
    [Test]
    public async Task Pair_without_its_whole_history_is_not_replayed()
    {
        _market.Keep([new ScannerPair(Wif, true, CoveredUntil: March1.AddDays(1))]);

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

    // Task 5.2: the buckets of the version over 90 and over the last 30 days, by the thresholds of now.
    [Test]
    public async Task Buckets_are_computed_over_90_and_30_days_by_the_thresholds_of_now()
    {
        var key = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.OneHour, IdeaSide.Long, MarketCategory.Linear);
        DateTimeOffset now = _time.GetUtcNow();
        _cases.CountAsync(2, now.AddDays(-90), Arg.Any<IReadOnlyDictionary<Interval, SetupThresholds>>(), Arg.Any<CancellationToken>())
            .Returns([new IdeaBucketCounts(key, 180, 200, 20, 2m, 40m)]);
        _cases.CountAsync(2, now.AddDays(-30), Arg.Any<IReadOnlyDictionary<Interval, SetupThresholds>>(), Arg.Any<CancellationToken>())
            .Returns([new IdeaBucketCounts(key, 6, 5, 1, 3m, 2.4m)]);

        await Replay(2).RecomputeBucketsAsync(CancellationToken.None);

        await _cases.Received(2).CountAsync(2, Arg.Any<DateTimeOffset>(), Arg.Is<IReadOnlyDictionary<Interval, SetupThresholds>>(thresholds => thresholds![Interval.OneHour] == new SetupThresholds(6, 3m, 2m, 1.5m, 3m)), Arg.Any<CancellationToken>());
        await _buckets.Received(1).ReplaceAsync(
            2,
            Arg.Is<IReadOnlyCollection<IdeaBucket>>(buckets => buckets!.Count == 2
                && buckets.Single(bucket => bucket.Days == 90).Statistics == BucketStatisticsCalculator.Compute(180, 200, 20, 2m, 40m)
                && buckets.Single(bucket => bucket.Days == 30).Statistics == BucketStatisticsCalculator.Compute(6, 5, 1, 3m, 2.4m)),
            Arg.Any<CancellationToken>());
    }

    // Decision 5: the percentile of a day comes from the candles before it; after the turn the last one goes to storage for the live ideas.
    [Test]
    public async Task Turn_stores_the_percentile_of_volatility_of_the_days_it_replayed()
    {
        _minutes = Waves(March1, March1.AddDays(2));
        Keep(March1.AddDays(2));
        _time.Advance(TimeSpan.FromDays(1));

        await Replay().StepAsync(CancellationToken.None);

        decimal expected = Volatility.Percentile(Volatility.Ratios(IdeaCandles.FromMinutes(Interval.FiveMinutes, _minutes, March1, March1.AddDays(2))))!.Value;
        await _volatility.Received(1).SaveAsync(
            Arg.Is<IReadOnlyCollection<IdeaVolatility>>(saved => saved!.Single(item => item.Interval == Interval.FiveMinutes).Percentile == expected
                && saved.Any(item => item.Interval == Interval.FifteenMinutes) && !saved.Any(item => item.Interval == Interval.OneHour)),
            Arg.Any<CancellationToken>());
    }

    private void Keep(DateTimeOffset coveredUntil)
    {
        _market.Keep([new ScannerPair(Wif, true, HistoryFrom: March1, CoveredUntil: coveredUntil)]);
    }

    private IdeasReplay Replay(int version = IdeasEngineVersion.Current)
    {
        var backtests = new BacktestSettings { DefaultFees = new Dictionary<string, FeeRateSettings> { ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m } } };

        return new IdeasReplay(
            _candles,
            _cases,
            _cursors,
            _volatility,
            _buckets,
            _market,
            Settings,
            backtests,
            new ScannerSettings(),
            new IdeasEngineVersion(version),
            _pace,
            _time);
    }

    // The live engine warmed up the same way at the start of the day and fed the same minutes.
    private IReadOnlyList<IdeaCase> Live()
    {
        var engine = new IdeaEngine(Settings.Live(), IdeasEngineVersion.Current);
        var closed = IdeasSettings.Intervals.ToDictionary(
            interval => interval,
            interval => (IReadOnlyList<IdeaCandle>)[.. IdeaCandles.FromMinutes(interval, _minutes, March1.AddDays(-100), March1).TakeLast(220)]);
        IdeaCandle[] before = [.. _minutes.Where(minute => minute.OpenTime < March1)];
        IReadOnlyList<IdeaCandle> warm = IdeaCandles.Minutes(before, before[0].OpenTime, March1, before[0].Open);
        engine.Warm(Wif, closed, warm, []);

        return
        [
            .. IdeaCandles.Minutes(_minutes, March1, March1.AddDays(1), warm[^1].Close)
                .SelectMany(minute => engine.Step(Wif, minute, IdeaInputs.None, true).Changed)
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