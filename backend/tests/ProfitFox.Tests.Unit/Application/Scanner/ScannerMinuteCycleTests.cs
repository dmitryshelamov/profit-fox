using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Minute;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner;

/// <summary>Requirements "Тикеры раз в минуту", "Снимок рынка" and "Состояние сканера".</summary>
public class ScannerMinuteCycleTests
{
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
        _feed.GetTickersAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns([new MarketTicker(Btc, 64000m, 63999m, 64001m)]);
        _feed.GetTickersAsync(MarketCategory.Linear, Arg.Any<CancellationToken>())
            .Returns([new MarketTicker(Sol, 150m, 149.9m, 150.1m, 1000m, 0.0001m), new MarketTicker(new Instrument(MarketCategory.Linear, "XRPUSDT"), 1m, 1m, 1m, 5m, 0m)]);
        _market.Keep([new ScannerPair(Sol, true, CoveredUntil: Minute, OpenInterestCoveredUntil: Minute), new ScannerPair(Btc, true, CoveredUntil: Minute.AddMinutes(-2))]);
        _market.TakeChanged();
        _cycle = new ScannerMinuteCycle(_feed, _openInterest, _snapshots, _pairs, _market, new ScannerSettings { SnapshotDelay = TimeSpan.FromSeconds(10) }, _identity, _time);
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
    }

    // Decision 2: a holder that lost the stream stops writing.
    [Test]
    public async Task Lost_lease_stops_the_cycle()
    {
        _snapshots.ReplaceAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<MarketSnapshotRow>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(false);

        await Should.ThrowAsync<ScannerLeaseLostException>(RunMinuteAsync);
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
}