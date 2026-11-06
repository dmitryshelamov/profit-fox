using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Fixtures.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner.Ideas;

/// <summary>
///     Requirement "Прогон по истории" and decision 4 of tune-idea-rules on the impulse set of <see cref="IdeaFixtures.Setup" />: the pair's
///     history starts on 2024-03-01, its minutes are stored up to the end of the day. The replay runs by the thresholds of the live ideas and
///     stores every signal with its path; the signals that count by the default exit rules close as the live ideas do.
/// </summary>
public class IdeasReplayTests
{
    private static readonly Instrument Wif = new Instrument(MarketCategory.Linear, "WIFUSDT");
    private static readonly DateTimeOffset March1 = DateTimeOffset.Parse("2024-03-01T00:00:00Z");
    private const decimal Tick = 0.01m;
    private static readonly IdeaExitRules Defaults = new IdeaExitRules([]);
    private static readonly IdeasSettings Settings = new IdeasSettings();

    private ICandleRepository _candles = null!;
    private IIdeaSignalRepository _signals = null!;
    private IIdeaReplayRepository _cursors = null!;
    private IIdeaVolatilityRepository _volatility = null!;
    private ScannerMarket _market = null!;
    private ScannerPace _pace = null!;
    private FakeTimeProvider _time = null!;
    // The signals as storage holds them, by key and firing: a write of one stored goes over it.
    private Dictionary<(IdeaKey, DateTimeOffset), IdeaSignal> _written = null!;
    private List<IReadOnlyCollection<IdeaSignal>> _writes = null!;
    private Dictionary<Instrument, IdeaReplayCursor> _stored = null!;
    private IReadOnlyList<IdeaCandle> _minutes = null!;

    [SetUp]
    public void SetUp()
    {
        _candles = Substitute.For<ICandleRepository>();
        _signals = Substitute.For<IIdeaSignalRepository>();
        _cursors = Substitute.For<IIdeaReplayRepository>();
        _volatility = Substitute.For<IIdeaVolatilityRepository>();
        _market = new ScannerMarket();
        _pace = new ScannerPace();
        _time = new FakeTimeProvider(March1.AddDays(1).AddMinutes(5));
        _written = [];
        _writes = [];
        _minutes = IdeaFixtures.Setup("impulse");
        _stored = [];
        _cursors.GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => new Dictionary<Instrument, IdeaReplayCursor>(_stored));
        _cursors.SetAsync(Arg.Any<Instrument>(), Arg.Any<int>(), Arg.Any<IdeaReplayCursor>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _stored[call.ArgAt<Instrument>(0)] = call.ArgAt<IdeaReplayCursor>(2);

            return Task.CompletedTask;
        });
        _signals.UpsertAsync(Arg.Any<IReadOnlyCollection<IdeaSignal>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            IdeaSignal[] batch = [.. call.Arg<IReadOnlyCollection<IdeaSignal>>()!];
            _writes.Add(batch);

            foreach (IdeaSignal signal in batch)
            {
                _written[(signal.Key, signal.Path.FiredAt)] = signal;
            }

            return Task.CompletedTask;
        });
        _candles.GetAsync(Wif, Interval.OneMinute, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
            (IReadOnlyList<Candle>)[.. _minutes.Where(minute => minute.OpenTime >= call.ArgAt<DateTimeOffset>(2) && minute.OpenTime < call.ArgAt<DateTimeOffset>(3)).Select(Candle)]);
        _candles.GetAggregatedAsync(Wif, Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(call =>
            IdeaCandles.FromMinutes(call.ArgAt<Interval>(1), _minutes, call.ArgAt<DateTimeOffset>(2), call.ArgAt<DateTimeOffset>(3)));
        Keep(March1.AddDays(1));
    }

    // As on the screen: the signals of a day that count by the default rules close as the live engine closes its ideas on the same minutes.
    [Test]
    public async Task Replay_of_a_day_closes_the_signals_that_count_as_the_live_engine_closes_its_ideas()
    {
        (await Replay().StepAsync(CancellationToken.None)).ShouldBeTrue();

        _written.ShouldNotBeEmpty();
        Outcomes(_written.Values).ShouldBe(Shape(Live()));
        await _cursors.Received(1).SetAsync(Wif, IdeasEngineVersion.Current, Until(March1.AddDays(1)), Arg.Any<CancellationToken>());
    }

    // Task 5.3: a path begun in one turn is written as far as it goes and written again by the next one; a path that ended is not written again.
    [Test]
    public async Task Next_turn_goes_on_with_the_paths_open_at_the_cursor()
    {
        IdeasReplay replay = Replay();
        await replay.StepAsync(CancellationToken.None);
        IdeaSignal[] open = [.. _stored[Wif].Open];
        IdeaSignal[] ended = [.. _written.Values.Where(signal => !signal.Path.IsOpen)];
        Keep(March1.AddDays(2));
        _time.Advance(TimeSpan.FromDays(1));

        (await replay.StepAsync(CancellationToken.None)).ShouldBeTrue();

        open.ShouldNotBeEmpty();
        open.ShouldAllBe(signal => signal.Path.TrackedUntil == March1.AddDays(1));
        open.Select(signal => _written[(signal.Key, signal.Path.FiredAt)].Path.TrackedUntil).ShouldAllBe(until => until > March1.AddDays(1));
        _writes.Skip(2).SelectMany(batch => batch).ShouldNotContain(signal => ended.Any(before => before.Key == signal.Key && before.Path.FiredAt == signal.Path.FiredAt));
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

        Outcomes(_written.Values).ShouldBe(Shape(Live()));
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
    // pair over from the start of its history; the paths it kept open are dropped.
    [Test]
    public async Task Cursor_older_than_the_history_starts_the_pair_over()
    {
        var stale = new IdeaSignal(Wif, IdeaSetup.Impulse, SignalPath.Fired(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m), Interval.OneHour, March1.AddDays(-40), Tick), 3m, 2m, IdeasEngineVersion.Current);
        _stored[Wif] = new IdeaReplayCursor(March1.AddDays(-30), [stale]);

        await Replay().StepAsync(CancellationToken.None);

        Outcomes(_written.Values).ShouldBe(Shape(Live()));
        await _candles.DidNotReceive().GetAsync(Wif, Interval.OneMinute, March1.AddDays(-30), March1.AddDays(-29), 1440, Arg.Any<CancellationToken>());
        _stored[Wif].Open.ShouldNotContain(signal => signal.Path.FiredAt == stale.Path.FiredAt);
        // Review of tune-idea-rules: the signals stored before are dropped first; a path left unfinished there would leave the signals of its
        // key after it undecided.
        string[] calls = [.. _signals.ReceivedCalls().Select(call => call.GetMethodInfo().Name)];
        calls.First(name => name is nameof(IIdeaSignalRepository.DeleteAsync) or nameof(IIdeaSignalRepository.UpsertAsync)).ShouldBe(nameof(IIdeaSignalRepository.DeleteAsync));
        await _signals.Received(1).DeleteAsync(Wif, IdeasEngineVersion.Current, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Pair_going_on_from_its_cursor_keeps_its_signals()
    {
        await Replay().StepAsync(CancellationToken.None);
        Keep(March1.AddDays(2));
        _time.Advance(TimeSpan.FromDays(1));

        await Replay().StepAsync(CancellationToken.None);

        await _signals.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default, default);
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

    // Scenario "Повторный прогон после смены правил": the signals and cursors of other versions go, so a new version replays from the start.
    // Prod held 24 M cases of version 4, more than one statement deletes within the command timeout, so a step removes an hour of them, and
    // the cursors go once no signal is left.
    [Test]
    public async Task Clean_up_removes_an_hour_of_other_versions_a_step_and_the_cursors_at_the_end()
    {
        _signals.DeleteOtherVersionsAsync(2, Arg.Any<CancellationToken>()).Returns(4, 0);
        IdeasReplay replay = Replay(2);

        (await replay.CleanUpAsync(CancellationToken.None)).ShouldBeTrue();
        await _cursors.DidNotReceive().DeleteOtherVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        (await replay.CleanUpAsync(CancellationToken.None)).ShouldBeFalse();

        await _signals.Received(2).DeleteOtherVersionsAsync(2, Arg.Any<CancellationToken>());
        await _cursors.Received(1).DeleteOtherVersionsAsync(2, Arg.Any<CancellationToken>());
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

    // Fix after review and task 5.3: the signals do not depend on how the days fall into turns. Four days of waves replayed in one turn and day by day,
    // each day by a replay of its own over the stored cursor; the third day starts without trades and opens 2 % up. The waves start 4 hours
    // before the history, so an idea of 15 minutes fired late on the second day closes on the third. Every third stretch of 211 minutes, the
    // steepest, trades three times the volume, so the candle setups have breakouts beside squeezes and RSI back from its zones.
    [Test]
    public async Task Replay_in_one_turn_and_day_by_day_gives_the_same_signals()
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
        string[] whole = [.. Shape(_written.Values)];
        IReadOnlyList<IdeaSignal> openAtTheEnd = _stored[Wif].Open;
        _written.Clear();
        _stored.Clear();

        for (DateTimeOffset until = March1.AddDays(1); until <= end; until = until.AddDays(1))
        {
            Keep(until);
            await Replay().StepAsync(CancellationToken.None);
        }

        whole.ShouldNotBeEmpty();
        new[] { IdeaSetup.Breakout, IdeaSetup.Squeeze, IdeaSetup.RsiExtreme }.ShouldAllBe(setup => _written.Values.Any(signal => signal.Setup == setup));
        Shape(_written.Values).ShouldBe(whole);
        Shape(_stored[Wif].Open).ShouldBe(Shape(openAtTheEnd));
        _written.Values.ShouldContain(signal => !signal.Path.IsOpen && signal.Path.FiredAt < Interval.OneDay.CandleOpen(signal.Path.TrackedUntil.AddTicks(-1)), "A path open across a cursor");
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
        string[] whole = [.. Shape(_written.Values)];
        _written.Clear();
        _stored.Clear();
        _candles.ClearReceivedCalls();

        for (DateTimeOffset until = March1.AddDays(1); until <= end; until = until.AddDays(1))
        {
            Keep(until);
            await Replay(settings: settings).StepAsync(CancellationToken.None);
        }

        Shape(_written.Values).ShouldBe(whole);
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
        return new IdeasReplay(
            _candles,
            _signals,
            _cursors,
            _volatility,
            _market,
            settings ?? Settings,
            new ScannerSettings(),
            new IdeasEngineVersion(version),
            _pace,
            _time);
    }

    // The ideas the live engine, warmed up the same way at the start of the day and fed the same minutes, closed on the day.
    private IReadOnlyList<Idea> Live()
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
                .Where(idea => !idea.IsOpen)
        ];
    }

    // How the ideas closed; the levels of one that never entered mean nothing.
    private static IEnumerable<string> Shape(IEnumerable<Idea> ideas)
    {
        return ideas.Select(idea => Shape(idea.Key, idea.Move, idea.VolumeRatio, idea.Track)).Order();
    }

    // The signals of each key that count by the default rules, closed as ideas.
    private static IEnumerable<string> Outcomes(IEnumerable<IdeaSignal> signals)
    {
        return signals.GroupBy(signal => signal.Key)
            .SelectMany(key => ExitOutcomes.Counted(key.OrderBy(signal => signal.Path.FiredAt).Select(signal => signal.Path), Defaults.Of(key.Key.Type, key.Key.Interval))
                .Select(counted => (Signal: key.Single(signal => signal.Path == counted.Path), counted.Outcome)))
            .Select(counted => Shape(counted.Signal.Key, counted.Signal.Move, counted.Signal.VolumeRatio, counted.Outcome))
            .Order();
    }

    private static string Shape(IdeaKey key, decimal move, decimal volumeRatio, IdeaTrack track)
    {
        IdeaTrack shown = track.EnteredAt is null ? track with { Plan = track.Plan with { Entry = 0m, Target = 0m } } : track;

        return $"{key} {move} {volumeRatio} {shown}";
    }

    private static IEnumerable<string> Shape(IEnumerable<IdeaSignal> signals)
    {
        return signals.Select(signal => $"{signal with { Path = signal.Path with { LadderAfter = [], Closes = [] } }} {string.Join(',', signal.Path.LadderAfter)} {string.Join(',', signal.Path.Closes)}").Order();
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