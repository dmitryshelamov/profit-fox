using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Ports.Market;
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
using ProfitFox.Domain.ValueObjects.Market;
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
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly DateTimeOffset March1 = DateTimeOffset.Parse("2024-03-01T00:00:00Z");
    private const decimal Tick = 0.01m;
    private static readonly IdeaExitRules Defaults = new IdeaExitRules([]);
    private static readonly IdeasSettings Settings = new IdeasSettings();

    private ICandleRepository _candles = null!;
    private IIdeaSignalRepository _signals = null!;
    private IIdeaReplayRepository _cursors = null!;
    private IIdeaVolatilityRepository _volatility = null!;
    private IFundingRateRepository _funding = null!;
    private ScannerMarket _market = null!;
    private ScannerPace _pace = null!;
    private IScannerJournal _journal = null!;
    private FakeTimeProvider _time = null!;
    // The signals as storage holds them, by key and firing: a write of one stored goes over it.
    private Dictionary<(IdeaKey, DateTimeOffset), IdeaSignal> _written = null!;
    private List<IReadOnlyCollection<IdeaSignal>> _writes = null!;
    private Dictionary<Instrument, IdeaReplayCursor> _stored = null!;
    private IReadOnlyList<IdeaCandle> _minutes = null!;
    private IReadOnlyList<IdeaCandle> _btc = null!;
    // The warm-up of a turn of a pair waits for its gate, so a test holds a turn while it starts the others.
    private Dictionary<Instrument, TaskCompletionSource> _gates = null!;

    [SetUp]
    public void SetUp()
    {
        _candles = Substitute.For<ICandleRepository>();
        _signals = Substitute.For<IIdeaSignalRepository>();
        _cursors = Substitute.For<IIdeaReplayRepository>();
        _volatility = Substitute.For<IIdeaVolatilityRepository>();
        _funding = Substitute.For<IFundingRateRepository>();
        _funding.GetAsync(Arg.Any<Instrument>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _market = new ScannerMarket();
        _pace = new ScannerPace();
        _journal = Substitute.For<IScannerJournal>();
        _time = new FakeTimeProvider(March1.AddDays(1).AddMinutes(5));
        _written = [];
        _writes = [];
        _minutes = IdeaFixtures.Setup("impulse");
        _btc = [];
        _gates = [];
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
        Serve(Wif, () => _minutes);
        _candles.GetAsync(Btc, Interval.OneMinute, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
            (IReadOnlyList<Candle>)[.. _btc.Where(minute => minute.OpenTime >= call.ArgAt<DateTimeOffset>(2) && minute.OpenTime < call.ArgAt<DateTimeOffset>(3)).Select(Candle)]);
        _candles.GetAggregatedAsync(Btc, Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(call =>
            IdeaCandles.FromMinutes(call.ArgAt<Interval>(1), _btc, call.ArgAt<DateTimeOffset>(2), call.ArgAt<DateTimeOffset>(3)));
        Keep(March1.AddDays(1));
    }

    // As on the screen: the signals of a day that count by the default rules close as the live engine closes its ideas on the same minutes.
    [Test]
    public async Task Replay_of_a_day_closes_the_signals_that_count_as_the_live_engine_closes_its_ideas()
    {
        (await Step(Replay())).ShouldBeTrue();

        _written.ShouldNotBeEmpty();
        Outcomes(_written.Values).ShouldBe(Shape(Live()));
        await _cursors.Received(1).SetAsync(Wif, IdeasEngineVersion.Current, Until(March1.AddDays(1)), Arg.Any<CancellationToken>());
    }

    // Task 5.3: a path begun in one turn is written as far as it goes and written again by the next one; a path that ended is not written again.
    [Test]
    public async Task Next_turn_goes_on_with_the_paths_open_at_the_cursor()
    {
        IdeasReplay replay = Replay();
        await Step(replay);
        IdeaSignal[] open = [.. _stored[Wif].Open];
        IdeaSignal[] ended = [.. _written.Values.Where(signal => !signal.Path.IsOpen)];
        Keep(March1.AddDays(2));
        _time.Advance(TimeSpan.FromDays(1));

        (await Step(replay)).ShouldBeTrue();

        open.ShouldNotBeEmpty();
        open.ShouldAllBe(signal => signal.Path.TrackedUntil == March1.AddDays(1));
        open.Select(signal => _written[(signal.Key, signal.Path.FiredAt)].Path.TrackedUntil).ShouldAllBe(until => until > March1.AddDays(1));
        _writes.Skip(2).SelectMany(batch => batch).ShouldNotContain(signal => ended.Any(before => before.Key == signal.Key && before.Path.FiredAt == signal.Path.FiredAt));
        // The next turn warms up on the two days before its cursor: the window of the change of a day is a minute longer than a day.
        await _candles.Received(1).GetAsync(Wif, Interval.OneMinute, March1, March1.AddDays(1), 1440, Arg.Any<CancellationToken>());
        await _candles.Received(1).GetAsync(Wif, Interval.OneMinute, March1.AddDays(-1), March1.AddDays(1), 2880, Arg.Any<CancellationToken>());
        await _cursors.Received(1).SetAsync(Wif, IdeasEngineVersion.Current, Until(March1.AddDays(1)), Arg.Any<CancellationToken>());
        await _cursors.Received(1).SetAsync(Wif, IdeasEngineVersion.Current, Until(March1.AddDays(2)), Arg.Any<CancellationToken>());
        (await Step(replay)).ShouldBeFalse();
    }

    // History may start before the first trade: the days before it pass, and the turn still moves the cursor over them.
    [Test]
    public async Task Days_before_the_first_minute_of_a_pair_pass_and_move_the_cursor()
    {
        _market.Keep([new ScannerPair(Wif, true, HistoryFrom: March1.AddDays(-3), CoveredUntil: March1.AddDays(1), TickSize: Tick)]);
        IdeasReplay replay = Replay();

        (await Step(replay)).ShouldBeTrue();

        Outcomes(_written.Values).ShouldBe(Shape(Live()));
        await _cursors.Received(1).SetAsync(Wif, IdeasEngineVersion.Current, Until(March1.AddDays(-2)), Arg.Any<CancellationToken>());
        (await Step(replay)).ShouldBeFalse();
    }

    // Pairs take turns: the one furthest behind goes first, a pair never replayed before all others.
    [Test]
    public async Task Pair_furthest_behind_takes_the_next_turn()
    {
        var sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
        _stored[Wif] = new IdeaReplayCursor(March1, []);
        _market.Keep([new ScannerPair(sol, true, HistoryFrom: March1, CoveredUntil: March1.AddDays(1), TickSize: Tick)]);

        await Step(Replay());

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

        await Step(Replay());

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
        await Step(Replay());
        Keep(March1.AddDays(2));
        _time.Advance(TimeSpan.FromDays(1));

        await Step(Replay());

        await _signals.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default, default);
    }

    // A pair whose history is not loaded whole waits for it.
    [Test]
    public async Task Pair_without_its_whole_history_is_not_replayed()
    {
        _market.Keep([new ScannerPair(Wif, true, CoveredUntil: March1.AddDays(1), TickSize: Tick)]);

        (await Step(Replay())).ShouldBeFalse();
    }

    // A pair whose price step the list of instruments has not told yet waits for it: a day replayed without ideas would never be replayed again.
    [Test]
    public async Task Pair_without_its_price_step_is_not_replayed()
    {
        _market.Keep([new ScannerPair(Wif, true, HistoryFrom: March1, CoveredUntil: March1.AddDays(1))]);

        (await Step(Replay())).ShouldBeFalse();
    }

    // Decision 1: the replay waits between days while the minute cycle is late.
    [Test]
    public async Task Replay_waits_while_the_minute_cycle_is_late()
    {
        _pace.Snapshot(_time.GetUtcNow(), _time.GetUtcNow().AddSeconds(3));
        Task<bool> step = Step(Replay());
        await Task.Delay(50);

        await _candles.DidNotReceive().GetAsync(Wif, Interval.OneMinute, March1, March1.AddDays(1), 1440, Arg.Any<CancellationToken>());

        _pace.Snapshot(_time.GetUtcNow(), _time.GetUtcNow());
        _time.Advance(TimeSpan.FromSeconds(10));
        (await step).ShouldBeTrue();
        _written.ShouldNotBeEmpty();
    }

    // Scenario "Ход в логе" (decision 1 of speed-up-ideas-replay): the end of a turn goes to the journal with the pair, the days it replayed and
    // the time of the engine, of the reads and of the writes, each never negative.
    [Test]
    public async Task Turn_writes_its_pair_days_and_timings_to_the_journal()
    {
        Keep(March1.AddDays(2));
        _time.Advance(TimeSpan.FromDays(1));

        await Step(Replay());

        _journal.Received(1).Replayed(Wif, 2, Arg.Is<TimeSpan>(engine => engine >= TimeSpan.Zero), Arg.Is<TimeSpan>(reads => reads >= TimeSpan.Zero), Arg.Is<TimeSpan>(writes => writes >= TimeSpan.Zero));
    }

    // Decision 1 of speed-up-ideas-replay: the paths are stepped lazily, so one whose window closes right at the end of a day (a pullback fired 7
    // days before, a minute before midnight, whose limit was never touched) ends with the day: written as ended, tracked until midnight, and
    // kept out of the cursor.
    [Test]
    public async Task Path_whose_window_closes_at_the_end_of_a_day_ends_with_the_day()
    {
        var plan = new IdeaPlan(IdeaType.Pullback, IdeaSide.Long, 10m, 9m, 12m);
        var waiting = new IdeaSignal(Wif, IdeaSetup.Impulse, SignalPath.Fired(plan, Interval.FifteenMinutes, March1.AddDays(-7).AddMinutes(-1), Tick) with { TrackedUntil = March1 }, 3m, 2m, IdeasEngineVersion.Current);
        _stored[Wif] = new IdeaReplayCursor(March1, [waiting]);

        await Step(Replay());

        IdeaSignal ended = _written[(waiting.Key, waiting.Path.FiredAt)];
        (ended.Path.IsOpen, ended.Path.EntryAfter, ended.Path.TrackedUntil).ShouldBe((false, null, March1.AddDays(1)));
        _writes[0].ShouldContain(ended);
        _stored[Wif].Open.ShouldNotContain(signal => signal.Path.FiredAt == waiting.Path.FiredAt);
    }

    // Scenario "Два хода одновременно" (decision 2 of speed-up-ideas-replay): with two turns the pairs furthest behind go at once, SOLUSDT never
    // replayed before WIFUSDT a day behind; PEPEUSDT, the least behind, waits while two turns run.
    [Test]
    public async Task Two_turns_take_the_two_pairs_furthest_behind_at_once()
    {
        var sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
        var pepe = new Instrument(MarketCategory.Linear, "PEPEUSDT");
        Serve(sol, () => _minutes);
        Serve(pepe, () => _minutes);
        _gates[Wif] = new TaskCompletionSource();
        _gates[sol] = new TaskCompletionSource();
        _stored[Wif] = new IdeaReplayCursor(March1.AddDays(-1), []);
        _stored[pepe] = new IdeaReplayCursor(March1, []);
        _market.Keep([.. new[] { Wif, sol, pepe }.Select(pair => new ScannerPair(pair, true, HistoryFrom: March1.AddDays(-2), CoveredUntil: March1.AddDays(1), TickSize: Tick))]);
        IdeasReplay replay = Replay();

        Task first = (await replay.StartAsync(CancellationToken.None))!;
        await WaitAsync(() => Started(sol));
        Started(Wif).ShouldBeFalse();
        Task second = (await replay.StartAsync(CancellationToken.None))!;
        await WaitAsync(() => Started(Wif));

        (await replay.StartAsync(CancellationToken.None)).ShouldBeNull();
        Started(pepe).ShouldBeFalse();
        _gates[Wif].SetResult();
        _gates[sol].SetResult();
        await Task.WhenAll(first, second);
        _stored[sol].ReplayedUntil.ShouldBe(March1.AddDays(1));
        _stored[Wif].ReplayedUntil.ShouldBe(March1.AddDays(1));
    }

    // A pair a turn runs is not given to another turn, however many may run.
    [Test]
    public async Task Pair_a_turn_runs_is_not_given_to_a_second_turn()
    {
        _gates[Wif] = new TaskCompletionSource();
        IdeasReplay replay = Replay(settings: new IdeasSettings { ReplayTurns = 3 });

        Task turn = (await replay.StartAsync(CancellationToken.None))!;

        (await replay.StartAsync(CancellationToken.None)).ShouldBeNull();
        _gates[Wif].SetResult();
        await turn;
        _written.ShouldNotBeEmpty();
    }

    // Once its turn ends, a pair behind its coverage again takes the next turn; one replayed up to its coverage does not.
    [Test]
    public async Task Pair_takes_a_turn_again_after_its_turn_ends_while_it_is_behind()
    {
        IdeasReplay replay = Replay();
        await (await replay.StartAsync(CancellationToken.None))!;
        (await replay.StartAsync(CancellationToken.None)).ShouldBeNull();
        Keep(March1.AddDays(2));
        _time.Advance(TimeSpan.FromDays(1));

        Task? next = await replay.StartAsync(CancellationToken.None);

        next.ShouldNotBeNull();
        await next;
        _stored[Wif].ReplayedUntil.ShouldBe(March1.AddDays(2));
    }

    // Scenario "Минутный цикл опоздал" (decision 2 of speed-up-ideas-replay): with the snapshot 3 seconds late neither of two turns starts its next
    // day; once the snapshot is back in time both go on.
    [Test]
    public async Task Late_minute_cycle_holds_both_turns()
    {
        var sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
        Serve(sol, () => _minutes);
        _market.Keep([.. new[] { Wif, sol }.Select(pair => new ScannerPair(pair, true, HistoryFrom: March1, CoveredUntil: March1.AddDays(1), TickSize: Tick))]);
        _pace.Snapshot(_time.GetUtcNow(), _time.GetUtcNow().AddSeconds(3));
        IdeasReplay replay = Replay();
        Task first = (await replay.StartAsync(CancellationToken.None))!;
        Task second = (await replay.StartAsync(CancellationToken.None))!;
        await WaitAsync(() => Started(Wif) && Started(sol));
        await Task.Delay(50);

        await _candles.DidNotReceive().GetAsync(Arg.Any<Instrument>(), Interval.OneMinute, March1, March1.AddDays(1), 1440, Arg.Any<CancellationToken>());

        _pace.Snapshot(_time.GetUtcNow(), _time.GetUtcNow());

        while (!(first.IsCompleted && second.IsCompleted))
        {
            _time.Advance(TimeSpan.FromSeconds(10));
            await Task.Delay(10);
        }

        await Task.WhenAll(first, second);
        _stored[Wif].ReplayedUntil.ShouldBe(March1.AddDays(1));
        _stored[sol].ReplayedUntil.ShouldBe(March1.AddDays(1));
    }

    // Decision 3 of speed-up-ideas-replay: the closes of BTC of a day are read once for every turn over that day.
    [Test]
    public async Task Turns_over_one_day_read_the_closes_of_btc_once()
    {
        var sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
        Serve(sol, () => _minutes);
        _market.Keep([.. new[] { Wif, sol }.Select(pair => new ScannerPair(pair, true, HistoryFrom: March1, CoveredUntil: March1.AddDays(1), TickSize: Tick))]);
        IdeasReplay replay = Replay();

        (await Step(replay)).ShouldBeTrue();
        (await Step(replay)).ShouldBeTrue();

        _stored.Keys.ShouldBe([Wif, sol], ignoreOrder: true);
        await _candles.Received(1).GetAsync(Btc, Interval.OneMinute, March1.AddMinutes(-240), March1.AddDays(1), 1680, Arg.Any<CancellationToken>());
    }

    // A read of BTC that failed is not kept: the turn fails, and the next turn over the day reads it again.
    [Test]
    public async Task Failed_read_of_btc_is_read_again_by_the_next_turn()
    {
        var failure = new InvalidOperationException("storage down");
        _candles.GetAsync(Btc, Interval.OneMinute, March1.AddMinutes(-240), March1.AddDays(1), 1680, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<Candle>>(failure), Task.FromResult<IReadOnlyList<Candle>>([]));
        IdeasReplay replay = Replay();

        (await Should.ThrowAsync<InvalidOperationException>(Step(replay))).ShouldBe(failure);
        (await Step(replay)).ShouldBeTrue();

        _written.ShouldNotBeEmpty();
        await _candles.Received(2).GetAsync(Btc, Interval.OneMinute, March1.AddMinutes(-240), March1.AddDays(1), 1680, Arg.Any<CancellationToken>());
    }

    // The closes of at most 16 days are kept, the oldest going first: a second turn over 17 days reads the first day again and the last not.
    [Test]
    public async Task Closes_of_btc_of_at_most_sixteen_days_are_kept()
    {
        var sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
        Serve(sol, () => _minutes);
        _market.Keep([.. new[] { Wif, sol }.Select(pair => new ScannerPair(pair, true, HistoryFrom: March1, CoveredUntil: March1.AddDays(17), TickSize: Tick))]);
        _time.Advance(TimeSpan.FromDays(17));
        IdeasReplay replay = Replay();

        await Step(replay);
        await Step(replay);

        await _candles.Received(2).GetAsync(Btc, Interval.OneMinute, March1.AddMinutes(-240), March1.AddDays(1), 1680, Arg.Any<CancellationToken>());
        await _candles.Received(1).GetAsync(Btc, Interval.OneMinute, March1.AddDays(16).AddMinutes(-240), March1.AddDays(17), 1680, Arg.Any<CancellationToken>());
    }

    // Decision 3: the funding of a pair is read once a turn, over its days and the 7 days before the first, a settlement an hour at the most.
    [Test]
    public async Task Turn_of_five_days_reads_the_funding_once()
    {
        Keep(March1.AddDays(5));
        _time.Advance(TimeSpan.FromDays(4));

        await Step(Replay());

        await _funding.Received(1).GetAsync(Arg.Any<Instrument>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _funding.Received(1).GetAsync(Wif, March1.AddDays(-7), March1.AddDays(5), 24 * 13, Arg.Any<CancellationToken>());
    }

    // The features of the signals are those of a read a day: the closes of BTC of each day, and of the funding read once only the settlements a
    // read of the day would give, from 7 days before it, so a settlement 6.5 days before the first day is seen on the first day and not after.
    [Test]
    public async Task Features_of_a_turn_are_those_of_a_read_a_day()
    {
        DateTimeOffset end = March1.AddDays(4);
        _minutes = Waves(March1.AddHours(-4), end);
        _btc = [.. Waves(March1.AddHours(-5), end).Select(minute => minute with { Close = minute.Close * 400m })];
        FundingRatePoint[] settlements = [new FundingRatePoint(March1.AddDays(-6.5), 0.0009m), new FundingRatePoint(March1.AddDays(2.5), 0.0001m)];
        _funding.GetAsync(Wif, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
            (IReadOnlyList<FundingRatePoint>)[.. settlements.Where(point => point.Time >= call.ArgAt<DateTimeOffset>(1) && point.Time < call.ArgAt<DateTimeOffset>(2)).Take(call.ArgAt<int>(3))]);
        _time.Advance(TimeSpan.FromDays(3));
        Keep(end);
        await Step(Replay());
        string[] whole = [.. Shape(_written.Values)];
        _written.Clear();
        _stored.Clear();

        for (DateTimeOffset until = March1.AddDays(1); until <= end; until = until.AddDays(1))
        {
            Keep(until);
            await Step(Replay());
        }

        _written.Values.ShouldContain(signal => signal.Funding == 0.0009m && signal.Path.FiredAt < March1.AddDays(1));
        _written.Values.ShouldContain(signal => signal.Funding == null && signal.Path.FiredAt >= March1.AddDays(1));
        _written.Values.ShouldContain(signal => signal.BtcChange != null && signal.BtcChange != 0m);
        Shape(_written.Values).ShouldBe(whole);
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

        await Step(Replay());

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
    // steepest, trades three times the volume, so the candle setups have breakouts beside squeezes and RSI back from its zones, and squeezes
    // with their volume on one side are accumulations or distributions.
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
        await Step(Replay());
        string[] whole = [.. Shape(_written.Values)];
        IReadOnlyList<IdeaSignal> openAtTheEnd = _stored[Wif].Open;
        _written.Clear();
        _stored.Clear();

        for (DateTimeOffset until = March1.AddDays(1); until <= end; until = until.AddDays(1))
        {
            Keep(until);
            await Step(Replay());
        }

        whole.ShouldNotBeEmpty();
        new[] { IdeaSetup.Breakout, IdeaSetup.Squeeze, IdeaSetup.RsiExtreme, IdeaSetup.Accumulation }.ShouldAllBe(setup => _written.Values.Any(signal => signal.Setup == setup));
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
        await Step(Replay(settings: settings));
        string[] whole = [.. Shape(_written.Values)];
        _written.Clear();
        _stored.Clear();
        _candles.ClearReceivedCalls();

        for (DateTimeOffset until = March1.AddDays(1); until <= end; until = until.AddDays(1))
        {
            Keep(until);
            await Step(Replay(settings: settings));
        }

        Shape(_written.Values).ShouldBe(whole);
        await _candles.Received(1).GetAsync(Wif, Interval.OneMinute, March1.AddDays(1), March1.AddDays(3), 2880, Arg.Any<CancellationToken>());
    }

    // Scenario "Признаки в прогоне": a settlement 5 minutes after the minute the first signal fired on is not seen by it; the one before is.
    [Test]
    public async Task Signal_takes_the_funding_settled_up_to_its_minute()
    {
        await Step(Replay());
        DateTimeOffset first = _written.Values.Min(signal => signal.Path.FiredAt);
        var later = new FundingRatePoint(first.AddMinutes(4), 0.0001m);
        _funding.GetAsync(Wif, March1.AddDays(-7), March1.AddDays(1), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new FundingRatePoint(March1.AddHours(-4), 0.0007m), later]);
        _written.Clear();
        _stored.Clear();

        await Step(Replay());

        _written.Values.Where(signal => signal.Path.FiredAt == first).ShouldAllBe(signal => signal.Funding == 0.0007m && signal.FeaturesWith(null).HighFunding);
        _written.Values.ShouldAllBe(signal => signal.Funding == (signal.Path.FiredAt > later.Time ? 0.0001m : 0.0007m));
        _written.Values.ShouldContain(signal => signal.Funding == 0.0001m);
    }

    // Scenario "BTC у spot-идеи" on the replay: BTC at 100 000 before March 1 and 98 700 from it; a spot pair reads no funding.
    [Test]
    public async Task Spot_signal_takes_the_change_of_btc_of_its_minute_and_no_funding()
    {
        var spot = new Instrument(MarketCategory.Spot, "WIFUSDT");
        _btc = [.. Enumerable.Range(0, 2 * 1440).Select(index => March1.AddDays(-1).AddMinutes(index)).Select(time => Flat(time, time < March1 ? 100_000m : 98_700m))];
        _candles.GetAsync(spot, Interval.OneMinute, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
            (IReadOnlyList<Candle>)[.. _minutes.Where(minute => minute.OpenTime >= call.ArgAt<DateTimeOffset>(2) && minute.OpenTime < call.ArgAt<DateTimeOffset>(3)).Select(Candle)]);
        _candles.GetAggregatedAsync(spot, Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(call =>
            IdeaCandles.FromMinutes(call.ArgAt<Interval>(1), _minutes, call.ArgAt<DateTimeOffset>(2), call.ArgAt<DateTimeOffset>(3)));
        _market.Keep([new ScannerPair(Wif, false), new ScannerPair(spot, true, HistoryFrom: March1, CoveredUntil: March1.AddDays(1), TickSize: Tick)]);

        await Step(Replay());

        IdeaSignal[] signals = [.. _written.Values];
        signals.ShouldNotBeEmpty();
        signals.ShouldAllBe(signal => signal.Instrument == spot && signal.Funding == null);
        signals.ShouldAllBe(signal => signal.BtcChange == (signal.Path.FiredAt.AddMinutes(-241) < March1 ? -0.013m : 0m));
        signals.ShouldContain(signal => signal.FeaturesWith(null).BtcDrop);
        await _funding.DidNotReceive().GetAsync(Arg.Any<Instrument>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // linear / BTCUSDT measures the change of BTC by its own minutes; without the minute 240 before, the change is unknown.
    [Test]
    public async Task Btc_signal_takes_the_change_of_its_own_minutes()
    {
        _btc = _minutes;
        _market.Keep([new ScannerPair(Wif, false), new ScannerPair(Btc, true, HistoryFrom: March1, CoveredUntil: March1.AddDays(1), TickSize: Tick)]);
        var closes = _minutes.ToDictionary(minute => minute.OpenTime, minute => minute.Close);

        await Step(Replay());

        IdeaSignal[] signals = [.. _written.Values];
        signals.ShouldNotBeEmpty();
        signals.ShouldAllBe(signal => signal.BtcChange == Change(closes, signal.Path.FiredAt.AddMinutes(-1)));
        signals.ShouldContain(signal => signal.BtcChange != null);
    }

    private static decimal? Change(Dictionary<DateTimeOffset, decimal> closes, DateTimeOffset minute)
    {
        return closes.TryGetValue(minute, out decimal now) && closes.TryGetValue(minute.AddMinutes(-240), out decimal before) ? now / before - 1m : null;
    }

    private static IdeaCandle Flat(DateTimeOffset time, decimal price)
    {
        return new IdeaCandle(time, price, price, price, price, 1m);
    }

    // A turn from its start to its end; false when none starts.
    private static async Task<bool> Step(IdeasReplay replay)
    {
        Task? turn = await replay.StartAsync(CancellationToken.None);

        if (turn is null)
        {
            return false;
        }

        await turn;

        return true;
    }

    // The stored minutes of a pair and its candles of the intervals, which the warm-up of a turn reads after its gate, if it has one.
    private void Serve(Instrument instrument, Func<IReadOnlyList<IdeaCandle>> minutes)
    {
        _candles.GetAsync(instrument, Interval.OneMinute, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
            (IReadOnlyList<Candle>)[.. minutes().Where(minute => minute.OpenTime >= call.ArgAt<DateTimeOffset>(2) && minute.OpenTime < call.ArgAt<DateTimeOffset>(3)).Select(Candle)]);
        _candles.GetAggregatedAsync(instrument, Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            if (_gates.TryGetValue(instrument, out TaskCompletionSource? gate))
            {
                await gate.Task;
            }

            return IdeaCandles.FromMinutes(call.ArgAt<Interval>(1), minutes(), call.ArgAt<DateTimeOffset>(2), call.ArgAt<DateTimeOffset>(3));
        });
    }

    // Whether a turn of the pair has started: its warm-up asked for its candles.
    private bool Started(Instrument instrument)
    {
        return _candles.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(ICandleRepository.GetAggregatedAsync) && Equals(call.GetArguments()[0], instrument));
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The condition did not hold in time.");
            }

            await Task.Delay(10);
        }
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
            _funding,
            _market,
            settings ?? Settings,
            new ScannerSettings(),
            new IdeasEngineVersion(version),
            _pace,
            _journal,
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