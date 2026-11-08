using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Backfill;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Application.Scanner.Minute;
using ProfitFox.Application.Scanner.Universe;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Unit.Application.Scanner.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner;

/// <summary>A failed step of the scanner is logged and tried again; only the end of the lease stops the session (task 8.3).</summary>
public class ScannerSessionTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-10T12:00:30Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Btc = new Instrument(MarketCategory.Spot, "BTCUSDT");
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private IMarketDataFeed _feed = null!;
    private ICandleRepository _candles = null!;
    private IMarketStreams _streams = null!;
    private IMarketSnapshotRepository _snapshots = null!;
    private IScannerJournal _journal = null!;
    private IIdeaRepository _ideaStore = null!;
    private IIdeaAutoEntryRepository _entries = null!;
    private IIdeaAutoRefusalRepository _refusals = null!;
    private IIdeaSignalRepository _ideaSignals = null!;
    private IIdeaReplayRepository _cursors = null!;
    private IIdeaExitRuleRepository _exitRules = null!;
    private IIdeaEngineVersionRepository _versions = null!;
    private IAsyncDisposable _subscription = null!;
    private ILiquidationRepository _liquidations = null!;
    private IStreamBreakRepository _breaks = null!;
    private IAsyncDisposable _liquidationSubscription = null!;
    private Action<Instrument, LiquidationEvent> _liquidated = null!;
    private Action<DateTimeOffset> _lost = null!;
    private Action<DateTimeOffset> _restored = null!;
    private Action<Action<Instrument, LiquidationEvent>>? _onLiquidationSubscribe;
    private ServiceProvider _meters = null!;
    private FakeTimeProvider _time = null!;
    private Action<Instrument, Candle> _closed = null!;
    private IReadOnlyList<ScannerPair> _stored = [];

    [SetUp]
    public void SetUp()
    {
        _feed = Substitute.For<IMarketDataFeed>();
        _feed.ListInstrumentsAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns([new ListedInstrument(MarketCategory.Spot, "BTCUSDT", "BTC", "USDT", true)]);
        _feed.ListInstrumentsAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns([]);
        _feed.GetTickersAsync(Arg.Any<MarketCategory>(), Arg.Any<CancellationToken>()).Returns([]);
        _feed.GetMinuteCandlesAsync(Arg.Any<Instrument>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        _candles = Substitute.For<ICandleRepository>();
        _streams = Substitute.For<IMarketStreams>();
        _subscription = Substitute.For<IAsyncDisposable>();
        _streams.SubscribeMinuteCandlesAsync(Arg.Any<IReadOnlyList<Instrument>>(), Arg.Any<Action<Instrument, Candle>>(), Arg.Any<Action>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _closed = call.ArgAt<Action<Instrument, Candle>>(1);

                return _subscription;
            });
        _meters = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _liquidations = Substitute.For<ILiquidationRepository>();
        _liquidations.AddAsync(Arg.Any<IReadOnlyCollection<(Instrument, LiquidationEvent)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<LiquidationSide, int>());
        _breaks = Substitute.For<IStreamBreakRepository>();
        _liquidationSubscription = Substitute.For<IAsyncDisposable>();
        _onLiquidationSubscribe = null;
        _streams.SubscribeLiquidationsAsync(
                Arg.Any<IReadOnlyList<Instrument>>(),
                Arg.Any<Action<Instrument, LiquidationEvent>>(),
                Arg.Any<Action<DateTimeOffset>>(),
                Arg.Any<Action<DateTimeOffset>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _liquidated = call.ArgAt<Action<Instrument, LiquidationEvent>>(1);
                _lost = call.ArgAt<Action<DateTimeOffset>>(2);
                _restored = call.ArgAt<Action<DateTimeOffset>>(3);
                _onLiquidationSubscribe?.Invoke(_liquidated);

                return _liquidationSubscription;
            });
        _snapshots = Substitute.For<IMarketSnapshotRepository>();
        _snapshots.ReplaceAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<MarketSnapshotRow>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        _snapshots.ReplaceSetupsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>>>(), Arg.Any<CancellationToken>()).Returns(true);
        _journal = Substitute.For<IScannerJournal>();
        _ideaStore = Substitute.For<IIdeaRepository>();
        _entries = Substitute.For<IIdeaAutoEntryRepository>();
        _refusals = Substitute.For<IIdeaAutoRefusalRepository>();
        _ideaStore.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([]);
        _ideaStore.SaveAsync(Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<CancellationToken>()).Returns([]);
        _ideaSignals = Substitute.For<IIdeaSignalRepository>();
        _ideaSignals.StreamAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<(IdeaType, Interval)?>(), Arg.Any<CancellationToken>())
            .Returns(_ => AsyncEnumerable.Empty<IdeaSignal>());
        _exitRules = ExitRules();
        _versions = Substitute.For<IIdeaEngineVersionRepository>();
        _cursors = Substitute.For<IIdeaReplayRepository>();
        _cursors.GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Instrument, IdeaReplayCursor>());
        _time = new FakeTimeProvider(Start);
        _closed = null!;
        _liquidated = null!;
        _stored = [];
    }

    [TearDown]
    public async Task TearDown()
    {
        await _subscription.DisposeAsync();
        await _liquidationSubscription.DisposeAsync();
        await _meters.DisposeAsync();
    }

    // Requirement "Начало версии правил": the session writes the start of its version of the rules when it takes the lease; storage keeps the first.
    [Test]
    public async Task Session_writes_the_start_of_its_version_of_the_rules_when_it_takes_the_lease()
    {
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);

        await WaitAsync(() => _versions.ReceivedCalls().Any());

        await _versions.Received(1).StartAsync(IdeasEngineVersion.Current, Start, Arg.Any<CancellationToken>());
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    [Test]
    public async Task Failed_write_of_closed_candles_is_logged_and_the_next_batch_goes_on()
    {
        _candles.UpsertAsync(Interval.OneMinute, Arg.Any<IReadOnlyCollection<(Instrument, Candle)>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("storage down")), Task.CompletedTask);
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);
        await WaitAsync(() => _closed is not null);

        _closed(Btc, Candle(Start.AddMinutes(-1)));
        await WaitAsync(() => _journal.ReceivedCalls().Any(call => (string)call.GetArguments()[0]! == "candles"));
        _closed(Btc, Candle(Start.AddMinutes(-1)));
        await WaitAsync(() => _candles.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ICandleRepository.UpsertAsync) && call.GetArguments().Length == 3 && call.GetArguments()[0] is Interval) == 2);

        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    [Test]
    public async Task Failed_rest_step_is_logged_and_tried_again()
    {
        _feed.GetMinuteCandlesAsync(Arg.Any<Instrument>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<Candle>>(new ExchangeUnavailableException("down")), Task.FromResult<IReadOnlyList<Candle>>([]));
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);

        await WaitAsync(() => _journal.ReceivedCalls().Any(call => (string)call.GetArguments()[0]! == "rest"));
        await WaitAsync(() => _feed.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IMarketDataFeed.GetMinuteCandlesAsync)) > 1, advance: true);

        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    [Test]
    public async Task Failed_minute_is_logged_and_the_next_minute_goes_on()
    {
        _snapshots.ReplaceAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<MarketSnapshotRow>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("storage down")), Task.FromResult(true));
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);

        await WaitAsync(() => _snapshots.ReceivedCalls().Count() >= 2, advance: true);

        _journal.Received().Failed("minute", Arg.Any<InvalidOperationException>());
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    // Requirement "Лимиты Bybit": during a pause of REST the stream still goes, on the pairs storage knows.
    [Test]
    public async Task Session_starts_on_the_stored_pairs_when_the_list_cannot_be_read_and_reads_it_later()
    {
        _feed.ListInstrumentsAsync(Arg.Any<MarketCategory>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<ListedInstrument>>(new ExchangeRateLimitedException("paused")), Task.FromResult<IReadOnlyList<ListedInstrument>>([]));
        _stored = [new ScannerPair(Btc, true)];
        using var lease = new CancellationTokenSource();

        Task session = Session().RunAsync(lease.Token);

        await WaitAsync(() => _closed is not null);
        await _streams.Received(1).SubscribeMinuteCandlesAsync(
            Arg.Is<IReadOnlyList<Instrument>>(instruments => instruments!.SequenceEqual(new[] { Btc })),
            Arg.Any<Action<Instrument, Candle>>(),
            Arg.Any<Action>(),
            Arg.Any<CancellationToken>());
        await WaitAsync(() => _feed.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IMarketDataFeed.ListInstrumentsAsync)) > 1, advance: true);
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    // Scenario "Новая пара": a subscription that fails after the list changed is made again, and the old one is closed once.
    [Test]
    public async Task Failed_subscription_after_a_change_of_the_list_is_made_again()
    {
        IAsyncDisposable first = Substitute.For<IAsyncDisposable>();
        IAsyncDisposable second = Substitute.For<IAsyncDisposable>();
        var calls = 0;
        _streams.SubscribeMinuteCandlesAsync(Arg.Any<IReadOnlyList<Instrument>>(), Arg.Any<Action<Instrument, Candle>>(), Arg.Any<Action>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls switch
            {
                1 => Task.FromResult(first),
                2 => Task.FromException<IAsyncDisposable>(new InvalidOperationException("the stream breaks")),
                _ => Task.FromResult(second)
            });
        _feed.ListInstrumentsAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns(
            [new ListedInstrument(MarketCategory.Spot, "BTCUSDT", "BTC", "USDT", true)],
            [new ListedInstrument(MarketCategory.Spot, "BTCUSDT", "BTC", "USDT", true), new ListedInstrument(MarketCategory.Spot, "ETHUSDT", "ETH", "USDT", true)]);
        using var lease = new CancellationTokenSource();
        // The list is read again each minute, so the clock does not have to pass an hour before the next subscription.
        Task session = Session(TimeSpan.FromMinutes(1)).RunAsync(lease.Token);

        await WaitAsync(() => calls >= 3, advance: true);
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();

        await first.Received(1).DisposeAsync();
        await second.Received(1).DisposeAsync();
    }

    // Decision 2: a holder that lost the stream takes it off and stops.
    [Test]
    public async Task Lost_lease_ends_the_session_and_takes_the_stream_off()
    {
        _snapshots.ReplaceAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<MarketSnapshotRow>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(false);

        Task session = Session().RunAsync(CancellationToken.None);
        await WaitAsync(() => session.IsCompleted, advance: true);

        await Should.ThrowAsync<ScannerLeaseLostException>(session);
        await _subscription.Received(1).DisposeAsync();
        await _liquidationSubscription.Received(1).DisposeAsync();
        _subscription.ClearReceivedCalls();
    }

    // Requirement "Прогон идей в фоне сканера": the lane of ideas starts by clearing other versions, then replays history, and a failure in it
    // is logged and tried again.
    [Test]
    public async Task Lane_of_ideas_clears_other_versions_and_logs_its_failure()
    {
        var failure = new InvalidOperationException("replay");
        _ideaSignals.DeleteOtherVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<int>(failure), Task.FromResult(0));
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => _journal.ReceivedCalls().Any(call => Equals(call.GetArguments()[0], "ideas-cleanup")));
        await WaitAsync(() => _ideaSignals.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IIdeaSignalRepository.DeleteOtherVersionsAsync)) == 2, advance: true);
        await WaitAsync(() => _cursors.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IIdeaReplayRepository.GetAsync)), advance: true);

        _journal.Received(1).Failed("ideas-cleanup", failure);
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // Deploy of v29: every step of the clean-up outran the command timeout, and while it failed the lane of ideas read no warm-ups, so prod made no
    // live idea for hours. The clean-up has a lane of its own: a failure of it is logged apart and the rest of the lane of ideas goes on.
    [Test]
    public async Task Failed_clean_up_of_other_versions_does_not_stop_the_lane_of_ideas()
    {
        var failure = new InvalidOperationException("timeout");
        _ideaSignals.DeleteOtherVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<int>(failure));
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => _ideaStore.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IIdeaRepository.DeleteClosedBeforeAsync)), advance: true);

        _journal.Received().Failed("ideas-cleanup", failure);
        _journal.DidNotReceive().Failed("ideas", Arg.Any<Exception>());
        await _cursors.DidNotReceive().GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // Deploy of v19: the clean-up of 24 M cases of version 4 kept the warm-ups of the live ideas waiting for hours. It goes an hour of signals a
    // step, so the warm-ups go between the steps, and the history is replayed once no signal of another version is left.
    [Test]
    public async Task Lane_of_ideas_clears_other_versions_step_by_step_before_the_history()
    {
        _ideaSignals.DeleteOtherVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(3, 2, 0);
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => _cursors.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IIdeaReplayRepository.GetAsync)), advance: true);

        _ideaSignals.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IIdeaSignalRepository.DeleteOtherVersionsAsync)).ShouldBe(3);
        await _cursors.Received(1).DeleteOtherVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // Scenario "Два хода одновременно" (decision 2 of speed-up-ideas-replay): the lane of ideas runs two turns at once, each on a pair of its own.
    [Test]
    public async Task Lane_of_ideas_runs_two_turns_at_once()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<IdeaCandle>>();
        ReplayPairs();
        _candles.GetAggregatedAsync(Arg.Any<Instrument>(), Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        using var cancellation = new CancellationTokenSource();
        Task session = Session(historyDays: 2).RunAsync(cancellation.Token);

        await WaitAsync(() => Warmed(Btc) > 0 && Warmed(Sol) > 0, advance: true);

        gate.SetResult([]);
        await WaitAsync(() => Replayed(Btc) && Replayed(Sol), advance: true);
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // A turn that fails is logged and its pair is taken again after the retry, while the other turn goes on.
    [Test]
    public async Task Failed_turn_is_logged_and_stops_neither_the_other_turn_nor_the_lane()
    {
        var failure = new InvalidOperationException("turn");
        var gate = new TaskCompletionSource<IReadOnlyList<IdeaCandle>>();
        ReplayPairs();
        _candles.GetAggregatedAsync(Btc, Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<IdeaCandle>>(failure));
        _candles.GetAggregatedAsync(Sol, Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        using var cancellation = new CancellationTokenSource();
        Task session = Session(historyDays: 2).RunAsync(cancellation.Token);

        await WaitAsync(() => Warmed(Sol) > 0 && Warmed(Btc) >= 2, advance: true);

        _journal.Received().Failed("ideas", failure);
        Warmed(Sol).ShouldBe(1);
        gate.SetResult([]);
        await WaitAsync(() => Replayed(Sol), advance: true);
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // The lane goes round while the turns run: the warm-ups of the live ideas are read and the closed ideas removed each hour without waiting
    // for the end of a turn.
    [Test]
    public async Task Lane_of_ideas_goes_on_while_the_turns_run()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<IdeaCandle>>();
        ReplayPairs();
        _candles.GetAggregatedAsync(Arg.Any<Instrument>(), Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        using var cancellation = new CancellationTokenSource();
        Task session = Session(historyDays: 2).RunAsync(cancellation.Token);
        await WaitAsync(() => Warmed(Btc) > 0 && Warmed(Sol) > 0, advance: true);

        await WaitAsync(() => _ideaStore.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IIdeaRepository.DeleteClosedBeforeAsync)) >= 2, advance: true);

        (Warmed(Btc), Warmed(Sol)).ShouldBe((1, 1));
        gate.SetResult([]);
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // Scenario "Минута во время пересчёта": the statistics have a lane of their own; while a recompute stands, the replay goes on.
    [Test]
    public async Task Replay_goes_on_while_the_statistics_are_recomputed()
    {
        var stuck = new TaskCompletionSource();
        _ideaSignals.StreamAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<(IdeaType, Interval)?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Stuck(stuck.Task));
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => _ideaSignals.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IIdeaSignalRepository.StreamAsync)));
        await WaitAsync(() => _cursors.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IIdeaReplayRepository.GetAsync)), advance: true);
        await WaitAsync(() => _snapshots.ReceivedCalls().Count() >= 2, advance: true);

        stuck.SetResult();
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // Scenario "Час без применений": the whole recompute starts the session and comes again an hour later, not before.
    // Deploy of v31: while the cases of version 5 were cleared, the recompute of version 6 read the whole table for cases it had none of yet (its
    // history is replayed after the clean-up) and outran the timeout every minute. The statistics wait for the clean-up, as the history does.
    [Test]
    public async Task Statistics_wait_for_the_clean_up_of_other_versions()
    {
        var cleaned = new TaskCompletionSource<int>();
        _ideaSignals.DeleteOtherVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => cleaned.Task);
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => _ideaSignals.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IIdeaSignalRepository.DeleteOtherVersionsAsync)));
        await Task.Delay(200);
        Recomputes(null).ShouldBe(0);
        cleaned.SetResult(0);
        await WaitAsync(() => Recomputes(null) == 1);

        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    [Test]
    public async Task Statistics_are_recomputed_once_an_hour()
    {
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => Recomputes(null) == 1);
        _time.Advance(TimeSpan.FromMinutes(59));
        await Task.Delay(200);
        Recomputes(null).ShouldBe(1);
        _time.Advance(TimeSpan.FromMinutes(1));
        await WaitAsync(() => Recomputes(null) == 2);

        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // Scenario "Пересчёт после применения": the minute after a rule is applied its buckets are recomputed by it.
    [Test]
    public async Task Buckets_of_a_rule_applied_are_recomputed_within_two_minutes()
    {
        DateTimeOffset appliedAt = Start.AddMinutes(1);
        var applied = new IdeaExitRule(IdeaType.Pullback, Interval.FifteenMinutes, 48, 2.5m, 8, appliedAt);
        _exitRules.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => _time.GetUtcNow() >= appliedAt ? [applied] : []);
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => Recomputes((IdeaType.Pullback, Interval.FifteenMinutes)) == 1, advance: true);

        (_time.GetUtcNow() - appliedAt).ShouldBeLessThan(TimeSpan.FromMinutes(2));
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // Review of tune-idea-rules: on the market of prod the whole recompute takes over a minute; the buckets of a rule applied meanwhile do not
    // wait for it. Decision 4 of honest-idea-intervals: they shrink to the categories of the whole pass before, so the first one has ended.
    [Test]
    public async Task Buckets_of_a_rule_applied_do_not_wait_for_the_whole_recompute()
    {
        var stuck = new TaskCompletionSource();
        DateTimeOffset appliedAt = Start.AddMinutes(61);
        var applied = new IdeaExitRule(IdeaType.Pullback, Interval.FifteenMinutes, 48, 2.5m, 8, appliedAt);
        _exitRules.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => _time.GetUtcNow() >= appliedAt ? [applied] : []);
        _ideaSignals.StreamAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<(IdeaType, Interval)?>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<(IdeaType, Interval)?>(2) is not null || Recomputes(null) == 1 ? AsyncEnumerable.Empty<IdeaSignal>() : Stuck(stuck.Task));
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);
        await WaitAsync(() => Recomputes(null) == 1);
        await Task.Delay(100);

        _time.Advance(TimeSpan.FromHours(1));
        await WaitAsync(() => Recomputes(null) == 2);
        await WaitAsync(() => Recomputes((IdeaType.Pullback, Interval.FifteenMinutes)) == 1, advance: true);

        Recomputes(null).ShouldBe(2);
        stuck.SetResult();
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // Review of tune-idea-rules: when the recompute of one type × interval fails, the ones taken with it are not lost: all are done after the
    // retry.
    [Test]
    public async Task Failed_recompute_of_a_type_and_interval_keeps_the_ones_taken_with_it()
    {
        DateTimeOffset appliedAt = Start.AddMinutes(1);
        IdeaExitRule[] applied =
        [
            new IdeaExitRule(IdeaType.Pullback, Interval.FifteenMinutes, 48, 2.5m, 8, appliedAt),
            new IdeaExitRule(IdeaType.TwoR, Interval.OneHour, 48, 3m, null, appliedAt)
        ];
        _exitRules.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => _time.GetUtcNow() >= appliedAt ? applied : []);
        var served = new List<(IdeaType, Interval)>();
        bool failed = false;
        _ideaSignals.StreamAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<(IdeaType, Interval)?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.ArgAt<(IdeaType, Interval)?>(2) is not { } only)
                {
                    return AsyncEnumerable.Empty<IdeaSignal>();
                }

                if (!failed)
                {
                    failed = true;

                    return Failing();
                }

                lock (served)
                {
                    served.Add(only);
                }

                return AsyncEnumerable.Empty<IdeaSignal>();
            });
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => { lock (served) { return served.Distinct().Count() == 2; } }, advance: true);

        _journal.Received().Failed("statistics", Arg.Any<InvalidOperationException>());
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // Requirement "Срок хранения закрытых идей": the lane of ideas removes the old closed ideas when the session starts and then once an hour.
    [Test]
    public async Task Lane_of_ideas_removes_the_old_closed_ideas_at_start_and_then_hourly()
    {
        var removed = new List<DateTimeOffset>();
        _ideaStore.DeleteClosedBeforeAsync(Arg.Do<DateTimeOffset>(removed.Add), Arg.Any<CancellationToken>()).Returns(0);
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => removed.Count == 1);
        removed[0].ShouldBe(Start.AddDays(-7));
        _time.Advance(TimeSpan.FromMinutes(59));
        await WaitAsync(() => removed.Count == 2, advance: true);

        (removed[1] - removed[0]).ShouldBeInRange(TimeSpan.FromHours(1), TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // Task 3.3: next to the closed ideas the lane deletes the refusals of the autos older than 7 days and turns the requests nobody took into refusals, hourly.
    [Test]
    public async Task Lane_of_ideas_cleans_the_refusals_and_the_requests_of_the_autos_with_the_closed_ideas()
    {
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => _refusals.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IIdeaAutoRefusalRepository.DeleteBeforeAsync)) == 1);
        await WaitAsync(() => _entries.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IIdeaAutoEntryRepository.ExpireAsync)) == 1);
        await _refusals.Received(1).DeleteBeforeAsync(Start.AddDays(-7), Arg.Any<CancellationToken>());
        _time.Advance(TimeSpan.FromMinutes(59));
        await WaitAsync(() => _refusals.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IIdeaAutoRefusalRepository.DeleteBeforeAsync)) == 2, advance: true);
        await WaitAsync(() => _entries.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IIdeaAutoEntryRepository.ExpireAsync)) == 2, advance: true);

        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // Task 3.3: a failure of one of the two is logged and does not stop the other or the ideas of the minute.
    [Test]
    public async Task Failed_clean_up_of_the_autos_is_logged_and_the_other_one_and_the_lane_go_on()
    {
        var failure = new InvalidOperationException("storage down");
        _refusals.DeleteBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<int>(failure));
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => _entries.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IIdeaAutoEntryRepository.ExpireAsync)), advance: true);
        await WaitAsync(() => _cursors.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IIdeaReplayRepository.GetAsync)), advance: true);

        _journal.Received(1).Failed("autos", failure);
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // Requirement "Срок хранения закрытых идей": a failed removal is logged, tried again, and the history goes on.
    [Test]
    public async Task Failed_removal_of_closed_ideas_is_logged_and_the_lane_goes_on()
    {
        var failure = new InvalidOperationException("storage down");
        _ideaStore.DeleteClosedBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<int>(failure), Task.FromResult(0));
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => _ideaStore.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IIdeaRepository.DeleteClosedBeforeAsync)) == 2, advance: true);
        await WaitAsync(() => _cursors.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IIdeaReplayRepository.GetAsync)), advance: true);

        _journal.Received(1).Failed("ideas", failure);
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // Requirement "Исход идеи на сделке": the trades made before the journal get the outcome of their idea once, when the session starts.
    [Test]
    public async Task Outcomes_of_trades_are_caught_up_once_when_the_session_starts()
    {
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => _ideaSignals.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IIdeaSignalRepository.DeleteOtherVersionsAsync)), advance: true);
        _time.Advance(TimeSpan.FromHours(3));
        await Task.Delay(100);

        await _ideaStore.Received(1).GetClosedOfTradesWithoutOutcomeAsync(Arg.Any<CancellationToken>());
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    // A failed catch-up is logged and the lane goes on with the clean-up and the history.
    [Test]
    public async Task Failed_catch_up_of_outcomes_is_logged_and_the_lane_goes_on()
    {
        var failure = new InvalidOperationException("storage down");
        _ideaStore.GetClosedOfTradesWithoutOutcomeAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<Idea>>(failure));
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => _ideaSignals.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IIdeaSignalRepository.DeleteOtherVersionsAsync)), advance: true);

        _journal.Received(1).Failed("trade-outcomes", failure);
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    private int Recomputes((IdeaType, Interval)? only)
    {
        return _ideaSignals.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IIdeaSignalRepository.StreamAsync)
                                                          && Equals(call.GetArguments()[2], only));
    }

    private static async IAsyncEnumerable<IdeaSignal> Failing()
    {
        await Task.Yield();

        throw new InvalidOperationException("storage down");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<IdeaSignal> Stuck(Task until)
    {
        await until;

        yield break;
    }

    // Requirement "Поток ликвидаций": the events of the stream go to storage in one batch per wake-up, like the candles.
    [Test]
    public async Task Liquidations_from_the_stream_go_to_storage_in_one_batch()
    {
        LinearListed("SOLUSDT");
        LiquidationEvent first = Liquidation(Start.AddSeconds(-2));
        LiquidationEvent second = Liquidation(Start.AddSeconds(-1));
        // Both come while the session subscribes, so the lane of liquidations finds them together.
        _onLiquidationSubscribe = liquidated =>
        {
            liquidated(Sol, first);
            liquidated(Sol, second);
        };
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);

        await WaitAsync(() => _liquidations.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(ILiquidationRepository.AddAsync)));

        var batch = (IReadOnlyCollection<(Instrument, LiquidationEvent)>)_liquidations.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(ILiquidationRepository.AddAsync)).GetArguments()[0]!;
        batch.ShouldBe([(Sol, first), (Sol, second)]);
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    // Scenario "Повтор не считается": only the rows storage added are counted.
    [Test]
    public async Task Liquidation_storage_already_had_is_not_counted()
    {
        LinearListed("SOLUSDT");
        using var collector = new MetricCollector<long>(_meters.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(), "ProfitFox.Scanner", "profitfox.scanner.liquidations");
        _liquidations.AddAsync(Arg.Any<IReadOnlyCollection<(Instrument, LiquidationEvent)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<LiquidationSide, int> { [LiquidationSide.Long] = 1 });
        _onLiquidationSubscribe = liquidated =>
        {
            liquidated(Sol, Liquidation(Start.AddSeconds(-2)));
            liquidated(Sol, Liquidation(Start.AddSeconds(-1)));
        };
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);

        await WaitAsync(() => collector.GetMeasurementSnapshot().Count > 0);

        collector.GetMeasurementSnapshot().Select(measurement => ((string)measurement.Tags["side"]!, measurement.Value)).ShouldBe([("long", 1L)]);
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    // Spot pairs have no liquidations.
    [Test]
    public async Task Only_linear_pairs_are_subscribed_for_liquidations()
    {
        LinearListed("SOLUSDT");
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);

        await WaitAsync(() => _liquidated is not null);

        await _streams.Received(1).SubscribeLiquidationsAsync(
            Arg.Is<IReadOnlyList<Instrument>>(instruments => instruments.SequenceEqual(new[] { Sol })),
            Arg.Any<Action<Instrument, LiquidationEvent>>(),
            Arg.Any<Action<DateTimeOffset>>(),
            Arg.Any<Action<DateTimeOffset>>(),
            Arg.Any<CancellationToken>());
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    // Scenario "Новая пара в списке": the liquidations follow the list as the candles do; the time between the old subscription and the new one
    // is a break, since the events of it are missed.
    [Test]
    public async Task New_pair_in_the_list_is_subscribed_for_liquidations_too()
    {
        var newPair = new Instrument(MarketCategory.Linear, "NEWUSDT");
        _feed.ListInstrumentsAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns(
            [Listed("SOLUSDT")],
            [Listed("SOLUSDT"), Listed("NEWUSDT")]);
        using var lease = new CancellationTokenSource();
        Task session = Session(TimeSpan.FromMinutes(1)).RunAsync(lease.Token);

        await WaitAsync(() => LiquidationSubscriptions().Any(instruments => instruments.Contains(newPair)), advance: true);
        await WaitAsync(() => _breaks.ReceivedCalls().Any());

        LiquidationSubscriptions()[^1].ShouldBe([Sol, newPair], ignoreOrder: true);
        await _liquidationSubscription.Received(1).DisposeAsync();
        var written = (StreamBreak)_breaks.ReceivedCalls().Single().GetArguments()[0]!;
        written.Stream.ShouldBe("liquidations");
        written.LostAt.ShouldBeLessThanOrEqualTo(written.RestoredAt);
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    // Both subscriptions go together: a refused subscription of liquidations after a change of the list takes its candles off, and the REST lane
    // makes both again.
    [Test]
    public async Task Refused_subscription_of_liquidations_takes_the_candles_off_and_is_made_again()
    {
        IAsyncDisposable refusedCandles = Substitute.For<IAsyncDisposable>();
        var candleCalls = 0;
        _streams.SubscribeMinuteCandlesAsync(Arg.Any<IReadOnlyList<Instrument>>(), Arg.Any<Action<Instrument, Candle>>(), Arg.Any<Action>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++candleCalls == 2 ? refusedCandles : _subscription);
        var liquidationCalls = 0;
        _streams.SubscribeLiquidationsAsync(
                Arg.Any<IReadOnlyList<Instrument>>(),
                Arg.Any<Action<Instrument, LiquidationEvent>>(),
                Arg.Any<Action<DateTimeOffset>>(),
                Arg.Any<Action<DateTimeOffset>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => ++liquidationCalls == 2
                ? Task.FromException<IAsyncDisposable>(new ExchangeUnavailableException("refused"))
                : Task.FromResult(_liquidationSubscription));
        _feed.ListInstrumentsAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns(
            [new ListedInstrument(MarketCategory.Spot, "BTCUSDT", "BTC", "USDT", true)],
            [new ListedInstrument(MarketCategory.Spot, "BTCUSDT", "BTC", "USDT", true), new ListedInstrument(MarketCategory.Spot, "ETHUSDT", "ETH", "USDT", true)]);
        using var lease = new CancellationTokenSource();
        Task session = Session(TimeSpan.FromMinutes(1)).RunAsync(lease.Token);

        await WaitAsync(() => liquidationCalls >= 3, advance: true);

        candleCalls.ShouldBe(3);
        await refusedCandles.Received(1).DisposeAsync();
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    // Decision 2: the batch cannot be loaded again, so a failed write is logged and its time is a break.
    [Test]
    public async Task Failed_write_of_liquidations_is_logged_and_its_events_are_a_break()
    {
        LinearListed("SOLUSDT");
        var failure = new InvalidOperationException("storage down");
        _liquidations.AddAsync(Arg.Any<IReadOnlyCollection<(Instrument, LiquidationEvent)>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyDictionary<LiquidationSide, int>>(failure));
        _onLiquidationSubscribe = liquidated =>
        {
            liquidated(Sol, Liquidation(Start.AddSeconds(-5)));
            liquidated(Sol, Liquidation(Start.AddSeconds(-1)));
        };
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);

        await WaitAsync(() => _breaks.ReceivedCalls().Any());

        _journal.Received(1).Failed("liquidations", failure);
        await _breaks.Received(1).AddAsync(new StreamBreak("liquidations", Start.AddSeconds(-5), Start.AddSeconds(-1)), Arg.Any<CancellationToken>());
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    // Scenario "Соединение восстановилось".
    [Test]
    public async Task Break_of_the_liquidation_stream_is_written_when_it_is_back()
    {
        LinearListed("SOLUSDT");
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);
        await WaitAsync(() => _liquidated is not null);

        _lost(DateTimeOffset.Parse("2024-03-10T03:10:05Z", CultureInfo.InvariantCulture));
        _restored(DateTimeOffset.Parse("2024-03-10T03:10:41Z", CultureInfo.InvariantCulture));
        await WaitAsync(() => _breaks.ReceivedCalls().Any());

        await _breaks.Received(1).AddAsync(
            new StreamBreak("liquidations", DateTimeOffset.Parse("2024-03-10T03:10:05Z", CultureInfo.InvariantCulture), DateTimeOffset.Parse("2024-03-10T03:10:41Z", CultureInfo.InvariantCulture)),
            Arg.Any<CancellationToken>());
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    // Scenario "Сканер сменил держателя": the break of the stream runs from its last stored event to the subscription of the new holder.
    [Test]
    public async Task New_holder_writes_a_break_from_the_last_stored_event_to_its_subscription()
    {
        LinearListed("SOLUSDT");
        _liquidations.LastTimeAsync(Arg.Any<CancellationToken>()).Returns(Start.AddMinutes(-7));
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);

        await WaitAsync(() => _breaks.ReceivedCalls().Any());

        await _breaks.Received(1).AddAsync(new StreamBreak("liquidations", Start.AddMinutes(-7), Start), Arg.Any<CancellationToken>());
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    // The first subscription ever has no stream before it to break.
    [Test]
    public async Task First_holder_without_stored_events_writes_no_break()
    {
        LinearListed("SOLUSDT");
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);

        await WaitAsync(() => _liquidated is not null);
        await Task.Delay(200);

        _breaks.ReceivedCalls().ShouldBeEmpty();
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    [Test]
    public async Task Failed_write_of_a_break_is_logged_and_the_next_break_goes_on()
    {
        LinearListed("SOLUSDT");
        var failure = new InvalidOperationException("storage down");
        _breaks.AddAsync(Arg.Any<StreamBreak>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(failure), Task.CompletedTask);
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);
        await WaitAsync(() => _liquidated is not null);

        _lost(Start.AddMinutes(-3));
        _restored(Start.AddMinutes(-2));
        await WaitAsync(() => _journal.ReceivedCalls().Any(call => Equals(call.GetArguments()[0], "stream-breaks")));
        _lost(Start.AddMinutes(-1));
        _restored(Start);
        await WaitAsync(() => _breaks.ReceivedCalls().Count() == 2);

        _journal.Received(1).Failed("stream-breaks", failure);
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    private void LinearListed(params string[] symbols)
    {
        _feed.ListInstrumentsAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns([.. symbols.Select(Listed)]);
    }

    private static ListedInstrument Listed(string symbol)
    {
        return new ListedInstrument(MarketCategory.Linear, symbol, symbol[..^4], "USDT", true, true);
    }

    private static LiquidationEvent Liquidation(DateTimeOffset at)
    {
        return new LiquidationEvent(at, LiquidationSide.Long, 150.2m, 12.5m);
    }

    private List<IReadOnlyList<Instrument>> LiquidationSubscriptions()
    {
        return [.. _streams.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IMarketStreams.SubscribeLiquidationsAsync))
            .Select(call => (IReadOnlyList<Instrument>)call.GetArguments()[0]!)];
    }

    private static IIdeaExitRuleRepository ExitRules()
    {
        IIdeaExitRuleRepository rules = Substitute.For<IIdeaExitRuleRepository>();
        rules.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        return rules;
    }

    /// <param name="universeRefresh">How often the session reads the list of pairs again; without it, the default hour.</param>
    /// <param name="historyDays">The days of history the scanner keeps, and the replay replays.</param>
    private ScannerSession Session(TimeSpan? universeRefresh = null, int historyDays = 1)
    {
        var market = new ScannerMarket();
        var settings = new ScannerSettings { HistoryDays = historyDays, UniverseRefresh = universeRefresh ?? new ScannerSettings().UniverseRefresh };
        var pace = new ScannerPace();
        var recomputes = new IdeaRecomputes();
        var ideas = new LiveIdeas(_ideaStore, Substitute.For<IIdeaVolatilityRepository>(), _candles, Substitute.For<IFundingRateRepository>(), _exitRules, recomputes, market, new IdeasSettings(), new BacktestSettings(), NoAutos.Create(market, new BacktestSettings(), _time, _entries, _refusals), _time);
        IScannerPairRepository pairs = Substitute.For<IScannerPairRepository>();
        pairs.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_stored);
        IScannerStateRepository state = Substitute.For<IScannerStateRepository>();

        return new ScannerSession(
            pairs,
            _candles,
            _streams,
            new ScannerUniverse(_feed, pairs, settings, _time),
            new ScannerBackfill(_feed, _candles, Substitute.For<IOpenInterestRepository>(), Substitute.For<IFundingRateRepository>(), pairs, state, market, settings, _time),
            new ScannerMinuteCycle(
                _feed,
                Substitute.For<IOpenInterestRepository>(),
                _snapshots,
                pairs,
                market,
                ideas,
                pace,
                _journal,
                settings,
                new WorkerIdentity(Guid.NewGuid()),
                _time),
            ideas,
            new IdeasReplay(
                _candles,
                _ideaSignals,
                _cursors,
                Substitute.For<IIdeaVolatilityRepository>(),
                Substitute.For<IFundingRateRepository>(),
                market,
                new IdeasSettings(),
                settings,
                new IdeasEngineVersion(IdeasEngineVersion.Current),
                pace,
                _journal,
                _time),
            new IdeaStatistics(
                _ideaSignals,
                _exitRules,
                Substitute.For<IIdeaBucketRepository>(),
                Substitute.For<IIdeaCalibrationRepository>(),
                _versions,
                new BacktestSettings(),
                new IdeasEngineVersion(IdeasEngineVersion.Current),
                _time),
            recomputes,
            market,
            _liquidations,
            _breaks,
            new ScannerMetrics(_meters.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            _journal,
            _time);
    }

    // BTCUSDT on spot and SOLUSDT on linear, with their history and price step, each with a day to replay when the scanner keeps 2 days.
    private void ReplayPairs()
    {
        _stored = [new ScannerPair(Btc, true, HistoryFrom: Start.AddDays(-2), CoveredUntil: Start, TickSize: 0.01m), new ScannerPair(Sol, true, HistoryFrom: Start.AddDays(-2), CoveredUntil: Start, TickSize: 0.01m)];
        _feed.ListInstrumentsAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns([new ListedInstrument(MarketCategory.Linear, "SOLUSDT", "SOL", "USDT", true, true)]);
        _candles.GetAsync(Arg.Any<Instrument>(), Interval.OneMinute, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    // How many turns of a pair warmed up: each asks for the candles of its intervals first.
    private int Warmed(Instrument instrument)
    {
        return _candles.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ICandleRepository.GetAggregatedAsync)
                                                      && Equals(call.GetArguments()[0], instrument) && Equals(call.GetArguments()[1], Interval.FiveMinutes));
    }

    private bool Replayed(Instrument instrument)
    {
        return _cursors.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IIdeaReplayRepository.SetAsync) && Equals(call.GetArguments()[0], instrument));
    }

    private async Task WaitAsync(Func<bool> condition, bool advance = false)
    {
        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The condition did not hold in time.");
            }

            if (advance)
            {
                _time.Advance(TimeSpan.FromSeconds(5));
            }

            await Task.Delay(10);
        }
    }

    private static Candle Candle(DateTimeOffset openTime)
    {
        return new Candle(openTime, new Price(1m), new Price(1m), new Price(1m), new Price(1m), new Volume(1m), new Volume(1m));
    }
}