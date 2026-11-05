using System.Globalization;

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
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner;

/// <summary>A failed step of the scanner is logged and tried again; only the end of the lease stops the session (task 8.3).</summary>
public class ScannerSessionTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-10T12:00:30Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Btc = new Instrument(MarketCategory.Spot, "BTCUSDT");

    private IMarketDataFeed _feed = null!;
    private ICandleRepository _candles = null!;
    private IMarketStreams _streams = null!;
    private IMarketSnapshotRepository _snapshots = null!;
    private IScannerJournal _journal = null!;
    private IIdeaRepository _ideaStore = null!;
    private IIdeaCaseRepository _ideaCases = null!;
    private IIdeaReplayRepository _cursors = null!;
    private IAsyncDisposable _subscription = null!;
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
        _snapshots = Substitute.For<IMarketSnapshotRepository>();
        _snapshots.ReplaceAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<MarketSnapshotRow>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        _snapshots.ReplaceSetupsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>>>(), Arg.Any<CancellationToken>()).Returns(true);
        _journal = Substitute.For<IScannerJournal>();
        _ideaStore = Substitute.For<IIdeaRepository>();
        _ideaCases = Substitute.For<IIdeaCaseRepository>();
        _cursors = Substitute.For<IIdeaReplayRepository>();
        _cursors.GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Instrument, IdeaReplayCursor>());
        _time = new FakeTimeProvider(Start);
        _closed = null!;
        _stored = [];
    }

    [TearDown]
    public async Task TearDown()
    {
        await _subscription.DisposeAsync();
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
        Task session = Session().RunAsync(lease.Token);

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
        _subscription.ClearReceivedCalls();
    }

    // Requirement "Прогон идей в фоне сканера": the lane of ideas starts by clearing other versions, recomputes the buckets every 10 minutes,
    // and a failure in it is logged and tried again.
    [Test]
    public async Task Lane_of_ideas_clears_other_versions_and_logs_its_failure()
    {
        var failure = new InvalidOperationException("replay");
        _ideaCases.DeleteOtherVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(failure), Task.CompletedTask);
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => _journal.ReceivedCalls().Any(call => Equals(call.GetArguments()[0], "ideas")));
        await WaitAsync(() => _ideaCases.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IIdeaCaseRepository.DeleteOtherVersionsAsync)) == 2, advance: true);
        await WaitAsync(() => _ideaCases.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IIdeaCaseRepository.CountAsync)), advance: true);

        _journal.Received(1).Failed("ideas", failure);
        int counts = _ideaCases.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IIdeaCaseRepository.CountAsync));
        await WaitAsync(() => _ideaCases.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IIdeaCaseRepository.CountAsync)) > counts, advance: true);
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

    // Requirement "Срок хранения закрытых идей": a failed removal is logged, tried again, and the history goes on.
    [Test]
    public async Task Failed_removal_of_closed_ideas_is_logged_and_the_lane_goes_on()
    {
        var failure = new InvalidOperationException("storage down");
        _ideaStore.DeleteClosedBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<int>(failure), Task.FromResult(0));
        using var cancellation = new CancellationTokenSource();
        Task session = Session().RunAsync(cancellation.Token);

        await WaitAsync(() => _ideaStore.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IIdeaRepository.DeleteClosedBeforeAsync)) == 2, advance: true);
        await WaitAsync(() => _ideaCases.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IIdeaCaseRepository.CountAsync)), advance: true);

        _journal.Received(1).Failed("ideas", failure);
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(session);
    }

    private ScannerSession Session()
    {
        var market = new ScannerMarket();
        var settings = new ScannerSettings { HistoryDays = 1 };
        var pace = new ScannerPace();
        var ideas = new LiveIdeas(_ideaStore, Substitute.For<IIdeaVolatilityRepository>(), _candles, market, new IdeasSettings(), _time);
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
                _ideaCases,
                _cursors,
                Substitute.For<IIdeaVolatilityRepository>(),
                Substitute.For<IIdeaBucketRepository>(),
                market,
                new IdeasSettings(),
                new BacktestSettings(),
                settings,
                new IdeasEngineVersion(IdeasEngineVersion.Current),
                pace,
                _time),
            market,
            _journal,
            _time);
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