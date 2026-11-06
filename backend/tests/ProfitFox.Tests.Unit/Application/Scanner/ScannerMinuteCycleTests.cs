using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Application.Scanner.Minute;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;
using ProfitFox.Tests.Unit.Application.Scanner.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner;

/// <summary>Requirements "Тикеры раз в минуту", "Снимок рынка", "Состояние сканера" and "Сетапы и идеи в минутном цикле".</summary>
public class ScannerMinuteCycleTests
{
    private static readonly BacktestSettings Fees = new BacktestSettings { DefaultFees = new Dictionary<string, FeeRateSettings> { ["spot"] = new FeeRateSettings { Limit = 0.1m, Market = 0.1m }, ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m } } };
    private static readonly DateTimeOffset Minute = DateTimeOffset.Parse("2024-03-10T12:05:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Spot, "BTCUSDT");

    private IMarketDataFeed _feed = null!;
    private IOpenInterestRepository _openInterest = null!;
    private IMarketSnapshotRepository _snapshots = null!;
    private IScannerPairRepository _pairs = null!;
    private ScannerMarket _market = null!;
    private FakeTimeProvider _time = null!;
    private WorkerIdentity _identity = null!;
    private IIdeaRepository _ideaStore = null!;
    private IScannerJournal _journal = null!;
    private LiveIdeas _ideas = null!;
    private ScannerPace _pace = null!;
    private ScannerMinuteCycle _cycle = null!;

    [SetUp]
    public void SetUp()
    {
        _feed = Substitute.For<IMarketDataFeed>();
        _openInterest = Substitute.For<IOpenInterestRepository>();
        _snapshots = Substitute.For<IMarketSnapshotRepository>();
        _pairs = Substitute.For<IScannerPairRepository>();
        _market = new ScannerMarket();
        _time = new FakeTimeProvider(Minute.AddSeconds(-30));
        _identity = new WorkerIdentity(Guid.NewGuid());
        _snapshots.ReplaceAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<MarketSnapshotRow>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        _snapshots.ReplaceSetupsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>>>(), Arg.Any<CancellationToken>()).Returns(true);
        _ideaStore = Substitute.For<IIdeaRepository>();
        _journal = Substitute.For<IScannerJournal>();
        _feed.GetTickersAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns([new MarketTicker(Btc, 64000m, 63999m, 64001m)]);
        _feed.GetTickersAsync(MarketCategory.Linear, Arg.Any<CancellationToken>())
            .Returns([new MarketTicker(Sol, 150m, 149.9m, 150.1m, 1000m, 0.0001m), new MarketTicker(new Instrument(MarketCategory.Linear, "XRPUSDT"), 1m, 1m, 1m, 5m, 0m)]);
        _market.Keep([new ScannerPair(Sol, true, CoveredUntil: Minute, OpenInterestCoveredUntil: Minute), new ScannerPair(Btc, true, CoveredUntil: Minute.AddMinutes(-2))]);
        _market.TakeChanged();
        _pace = new ScannerPace();
        _ideas = new LiveIdeas(_ideaStore, Substitute.For<IIdeaVolatilityRepository>(), Substitute.For<ICandleRepository>(), ExitRules(), new IdeaRecomputes(), _market, new IdeasSettings(), Fees, NoAutos.Create(_market, Fees, _time), _time);
        _cycle = new ScannerMinuteCycle(
            _feed,
            _openInterest,
            _snapshots,
            _pairs,
            _market,
            _ideas,
            _pace,
            _journal,
            new ScannerSettings { SnapshotDelay = TimeSpan.FromSeconds(10) },
            _identity,
            _time);
    }

    // Scenario "Открытый интерес из тикеров": the first read after the start of 5 minutes stores a point of every linear pair the scanner keeps.
    [Test]
    public async Task Tickers_of_each_category_are_read_once_a_minute_and_store_the_point_of_the_new_step()
    {
        await RunMinuteAsync();
        await RunMinuteAsync();

        await _feed.Received(2).GetTickersAsync(MarketCategory.Spot, Arg.Any<CancellationToken>());
        await _feed.Received(2).GetTickersAsync(MarketCategory.Linear, Arg.Any<CancellationToken>());
        await _openInterest.Received(1).AddAsync(Sol, Arg.Is<IReadOnlyCollection<OpenInterestPoint>>(points => points!.Single() == new OpenInterestPoint(Minute, 1000m)), Arg.Any<CancellationToken>());
        await _openInterest.DidNotReceive().AddAsync(new Instrument(MarketCategory.Linear, "XRPUSDT"), Arg.Any<IReadOnlyCollection<OpenInterestPoint>>(), Arg.Any<CancellationToken>());
        _market.Find(Sol)!.OpenInterestCoveredUntil.ShouldBe(Minute.AddMinutes(5));
    }

    // A start in the middle of a step reads the tickers after its first minute: their open interest is not the one of its start.
    [Test]
    public async Task Tickers_read_after_the_first_minute_of_a_step_store_no_point()
    {
        _time.Advance(TimeSpan.FromMinutes(2));

        await RunMinuteAsync();

        await _openInterest.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default);
        _market.Find(Sol)!.OpenInterestCoveredUntil.ShouldBe(Minute);
    }

    // Scenario "Снимок обновился": rows of every pair on the minutes up to 12:04 inclusive, computed after 12:05:00.
    [Test]
    public async Task Snapshot_of_the_minute_replaces_the_previous_one_after_the_delay()
    {
        await RunMinuteAsync();

        await _snapshots.Received(1).ReplaceAsync(
            _identity.Id,
            Arg.Is<IReadOnlyCollection<MarketSnapshotRow>>(rows =>
                rows!.Count == 2
                && rows.All(row => row.ComputedAt == Minute.AddSeconds(10))
                && rows.Single(row => row.Instrument == Sol).Metrics.Price == 150m
                && rows.Single(row => row.Instrument == Btc).Incomplete),
            Minute.AddSeconds(10),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Pairs_whose_coverage_lags_behind_are_told_and_changed_pairs_are_saved()
    {
        IReadOnlyList<Instrument> lagging = await RunMinuteAsync();

        lagging.ShouldBe([Btc]);
        await _pairs.Received(1).SaveAsync(Arg.Is<IReadOnlyCollection<ScannerPair>>(saved => saved!.Single().Instrument == Sol), Arg.Any<CancellationToken>());
    }

    // Requirement "Лимиты Bybit": the snapshot goes on while the REST requests of the scanner pause.
    [Test]
    public async Task Snapshot_is_made_with_the_last_tickers_while_the_exchange_does_not_answer()
    {
        _feed.GetTickersAsync(Arg.Any<MarketCategory>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ExchangeRateLimitedException("paused"));

        await RunMinuteAsync();

        await _snapshots.Received(1).ReplaceAsync(_identity.Id, Arg.Any<IReadOnlyCollection<MarketSnapshotRow>>(), Minute.AddSeconds(10), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Snapshot_follows_right_away_when_the_tickers_take_longer_than_the_delay()
    {
        _feed.GetTickersAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15), _time);

            return (IReadOnlyList<MarketTicker>)[];
        });

        await RunMinuteAsync();

        await _snapshots.Received(1).ReplaceAsync(_identity.Id, Arg.Any<IReadOnlyCollection<MarketSnapshotRow>>(), Minute.AddSeconds(15), Arg.Any<CancellationToken>());
        _pace.IsLate.ShouldBeTrue();
    }

    // Decision 1: the replay of history pauses while the snapshot comes late; one on time lets it go on.
    [Test]
    public async Task Snapshot_on_time_tells_the_replay_it_may_go_on()
    {
        _pace.Snapshot(Minute, Minute.AddSeconds(5));

        await RunMinuteAsync();

        _pace.IsLate.ShouldBeFalse();
    }

    // Decision 2: a holder that lost the stream stops writing.
    [Test]
    public async Task Lost_lease_stops_the_cycle()
    {
        _snapshots.ReplaceAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<MarketSnapshotRow>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(false);

        await Should.ThrowAsync<ScannerLeaseLostException>(RunMinuteAsync);
    }

    // Requirement "Сетапы и идеи в минутном цикле": after the snapshot the setups of the complete pairs go into their rows.
    [Test]
    public async Task Setups_of_the_complete_pairs_go_into_the_snapshot_after_it()
    {
        _market.Loaded(Sol, Minute.AddDays(-1), Minute, Flat(Minute.AddDays(-1), Minute));
        await _ideas.ReadWarmUpsAsync(CancellationToken.None);
        await RunMinuteAsync();

        _market.Closed(Sol, Flat(Minute, Minute.AddMinutes(1)).Single());
        await RunMinuteAsync();

        await _snapshots.Received(1).ReplaceSetupsAsync(
            _identity.Id,
            Arg.Is<IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>>>(setups => setups!.Keys.Single() == Sol && setups[Sol].Count == 0),
            Arg.Any<CancellationToken>());
    }

    // Scenario "Пропуск у пары": while the gap is loaded the pair gets no setups; once it is, its open idea moves by the minutes of the gap.
    [Test]
    public async Task Pair_with_a_gap_gets_no_setups_and_its_ideas_move_once_the_gap_is_loaded()
    {
        Idea open = IdeaGenerator.Generate(Minute.AddMinutes(-2), instrument: Btc);
        _ideaStore.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([open]);
        _market.Loaded(Btc, Minute.AddDays(-1), Minute.AddMinutes(-2), Flat(Minute.AddDays(-1), Minute.AddMinutes(-2)));
        await _ideas.ReadWarmUpsAsync(CancellationToken.None);

        await RunMinuteAsync();

        await _snapshots.Received(1).ReplaceSetupsAsync(_identity.Id, Arg.Is<IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>>>(setups => setups!.Count == 0), Arg.Any<CancellationToken>());
        await _ideaStore.DidNotReceive().SaveAsync(Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Is<IReadOnlyCollection<Idea>>(ideas => ideas!.Count > 0), Arg.Any<IReadOnlyCollection<ClosedIdea>>(), Arg.Any<CancellationToken>());

        _market.Loaded(Btc, Minute.AddMinutes(-2), Minute.AddMinutes(1), Flat(Minute.AddMinutes(-2), Minute.AddMinutes(1), 102m));
        await RunMinuteAsync();

        await _ideaStore.Received(1).SaveAsync(
            Arg.Any<IReadOnlyCollection<Idea>>(),
            Arg.Is<IReadOnlyCollection<Idea>>(ideas => ideas!.Single().Id == open.Id && ideas!.Single().Track.EnteredAt == Minute.AddMinutes(-2)),
            Arg.Any<IReadOnlyCollection<ClosedIdea>>(), Arg.Any<CancellationToken>());
        await _snapshots.Received(1).ReplaceSetupsAsync(_identity.Id, Arg.Is<IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>>>(setups => setups!.ContainsKey(Btc)), Arg.Any<CancellationToken>());
    }

    // Scenario "Сбой расчёта идей".
    [Test]
    public async Task Failure_of_the_ideas_leaves_the_snapshot_and_is_logged()
    {
        var failure = new InvalidOperationException("ideas");
        _ideaStore.GetOpenAsync(Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        IReadOnlyList<Instrument> lagging = await RunMinuteAsync();

        lagging.ShouldBe([Btc]);
        await _snapshots.Received(1).ReplaceAsync(_identity.Id, Arg.Any<IReadOnlyCollection<MarketSnapshotRow>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _snapshots.DidNotReceiveWithAnyArgs().ReplaceSetupsAsync(default, default!, default);
        _journal.Received(1).Failed("ideas", failure);
    }

    // Decision 2: the transaction of the minute locks the lease of the stream, and a scanner that lost it stops, not logs.
    [Test]
    public async Task Lost_lease_on_the_write_of_the_ideas_stops_the_cycle()
    {
        _ideaStore.SaveAsync(Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<IReadOnlyCollection<ClosedIdea>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ScannerLeaseLostException());

        await Should.ThrowAsync<ScannerLeaseLostException>(RunMinuteAsync);
        _journal.DidNotReceiveWithAnyArgs().Failed(default!, default!);
    }

    [Test]
    public async Task Lost_lease_on_the_setups_stops_the_cycle()
    {
        _snapshots.ReplaceSetupsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>>>(), Arg.Any<CancellationToken>()).Returns(false);

        await Should.ThrowAsync<ScannerLeaseLostException>(RunMinuteAsync);
    }

    // Flat minutes with a volume of 1.
    private static IEnumerable<Candle> Flat(DateTimeOffset from, DateTimeOffset to, decimal price = 100m)
    {
        for (DateTimeOffset time = from; time < to; time = time.AddMinutes(1))
        {
            yield return new Candle(time, new Price(price), new Price(price), new Price(price), new Price(price), new Volume(1m), new Volume(100m));
        }
    }

    // Moves the clock until the minute of the cycle is done.
    private async Task<IReadOnlyList<Instrument>> RunMinuteAsync()
    {
        Task<IReadOnlyList<Instrument>> minute = _cycle.RunMinuteAsync(CancellationToken.None);

        while (!minute.IsCompleted)
        {
            await Task.Delay(5);
            _time.Advance(TimeSpan.FromSeconds(5));
        }

        return await minute;
    }

    private static IIdeaExitRuleRepository ExitRules()
    {
        IIdeaExitRuleRepository rules = Substitute.For<IIdeaExitRuleRepository>();
        rules.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        return rules;
    }
}