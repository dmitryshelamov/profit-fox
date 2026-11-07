using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Backfill;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Fixtures.Market;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner;

/// <summary>Requirements "История за 90 дней" and "Пропуск после перерыва", decision 8: the order of the history load.</summary>
public class ScannerBackfillTests
{
    private static readonly DateTimeOffset End = DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Spot, "BTCUSDT");

    private IMarketDataFeed _feed = null!;
    private ICandleRepository _candles = null!;
    private IOpenInterestRepository _openInterest = null!;
    private IFundingRateRepository _funding = null!;
    private IScannerPairRepository _pairs = null!;
    private IScannerStateRepository _state = null!;
    private FakeTimeProvider _time = null!;
    private ScannerMarket _market = null!;
    private List<string> _requests = null!;

    // The exchange knows every minute and every point since this time.
    private DateTimeOffset _listedSince;
    private HashSet<DateTimeOffset> _quietMinutes = null!;

    [SetUp]
    public void SetUp()
    {
        _feed = Substitute.For<IMarketDataFeed>();
        _candles = Substitute.For<ICandleRepository>();
        _openInterest = Substitute.For<IOpenInterestRepository>();
        _funding = Substitute.For<IFundingRateRepository>();
        _pairs = Substitute.For<IScannerPairRepository>();
        _state = Substitute.For<IScannerStateRepository>();
        _time = new FakeTimeProvider(End.AddSeconds(20));
        _market = new ScannerMarket();
        _requests = [];
        _listedSince = DateTimeOffset.MinValue;
        _quietMinutes = [];

        _feed.GetMinuteCandlesAsync(Arg.Any<Instrument>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Candles(call.ArgAt<Instrument>(0), call.ArgAt<DateTimeOffset>(1), call.ArgAt<DateTimeOffset>(2))));
        _feed.GetOpenInterestAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Points(call.ArgAt<string>(0), call.ArgAt<DateTimeOffset>(1), call.ArgAt<DateTimeOffset>(2))));
        _feed.GetFundingHistoryAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Rates(call.ArgAt<string>(0), call.ArgAt<DateTimeOffset>(1), call.ArgAt<DateTimeOffset>(2))));
        _candles.GetAsync(Arg.Any<Instrument>(), Interval.OneMinute, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<Candle>>([.. Minutes(call.ArgAt<DateTimeOffset>(2), call.ArgAt<DateTimeOffset>(3)).Select(Minute)]));
        _openInterest.GetAsync(Arg.Any<Instrument>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OpenInterestPoint>>([]));
    }

    // Decision 8: the last day of every pair first, then the rest of the window pair by pair from new to old, then the funding of linear.
    [Test]
    public async Task Last_day_of_every_pair_comes_first_then_each_pair_back_to_the_start_of_the_window_then_its_funding()
    {
        _market.Keep([new ScannerPair(Btc, true), new ScannerPair(Sol, true)]);

        await RunAsync(new ScannerSettings { HistoryDays = 2 });

        _requests.ShouldBe(
        [
            "candles spot BTCUSDT 2024-03-09T19:20 2024-03-10T12:00", "candles spot BTCUSDT 2024-03-09T12:00 2024-03-09T19:20",
            "candles linear SOLUSDT 2024-03-09T19:20 2024-03-10T12:00", "candles linear SOLUSDT 2024-03-09T12:00 2024-03-09T19:20",
            "open-interest SOLUSDT 2024-03-09T11:55 2024-03-10T12:00",
            "candles spot BTCUSDT 2024-03-08T19:20 2024-03-09T12:00", "candles spot BTCUSDT 2024-03-08T12:00 2024-03-08T19:20",
            "candles linear SOLUSDT 2024-03-08T19:20 2024-03-09T12:00", "candles linear SOLUSDT 2024-03-08T12:00 2024-03-08T19:20",
            "open-interest SOLUSDT 2024-03-08T12:00 2024-03-09T11:55",
            "funding SOLUSDT 2024-03-08T12:00 2024-03-10T12:00"
        ]);
    }

    [Test]
    public async Task Pair_loaded_whole_records_the_start_of_its_history_and_the_progress()
    {
        _market.Keep([new ScannerPair(Btc, true), new ScannerPair(Sol, true)]);

        await RunAsync(new ScannerSettings { HistoryDays = 2 });

        ScannerPair sol = _market.Find(Sol)!;
        (sol.HistoryFrom, sol.CoveredUntil, sol.OpenInterestCoveredUntil, sol.FundingCoveredUntil).ShouldBe((End.AddDays(-2), End, End, End));
        await _pairs.Received().SaveAsync(Arg.Is<IReadOnlyCollection<ScannerPair>>(saved => saved!.Single() == sol), Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            _state.SetProgressAsync(2, 0, Arg.Any<CancellationToken>());
            _state.SetProgressAsync(2, 1, Arg.Any<CancellationToken>());
            _state.SetProgressAsync(2, 2, Arg.Any<CancellationToken>());
        });
    }

    // Requirement "Загрузка истории на экране": the last day of 1,100 pairs takes many minutes, and the screen counts them from the start.
    [Test]
    public async Task Progress_counts_the_pairs_before_the_last_day_of_any_pair_is_requested()
    {
        _market.Keep([new ScannerPair(Btc, true), new ScannerPair(Sol, true)]);
        _state.SetProgressAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _requests.Add($"progress {call.ArgAt<int>(0)} {call.ArgAt<int>(1)}");

                return Task.CompletedTask;
            });

        await RunAsync(new ScannerSettings { HistoryDays = 2 });

        _requests[0].ShouldBe("progress 2 0");
    }

    // Requirement "История за 90 дней": the history of a younger pair starts with its first candle.
    [Test]
    public async Task History_of_a_pair_younger_than_the_window_starts_with_its_launch()
    {
        _listedSince = End.AddDays(-1).AddMinutes(-500);
        _market.Keep([new ScannerPair(Sol, true, ListedAt: _listedSince)]);

        await RunAsync(new ScannerSettings { HistoryDays = 90 });

        _market.Find(Sol)!.HistoryFrom.ShouldBe(_listedSince);
        _requests.ShouldContain($"candles linear SOLUSDT {Text(_listedSince)} 2024-03-09T12:00");
        _requests.Where(request => request.StartsWith("candles", StringComparison.Ordinal)).ShouldAllBe(request => string.CompareOrdinal(request.Split(' ')[3], Text(_listedSince)) >= 0);
        _requests.ShouldContain($"funding SOLUSDT {Text(_listedSince)} 2024-03-10T12:00");
    }

    // A thin spot pair may trade nothing for 1000 minutes; the exchange tells no launch of a spot pair, so the window goes on to its start.
    [Test]
    public async Task Page_without_trades_does_not_end_the_history_of_a_pair()
    {
        for (DateTimeOffset minute = End.AddDays(-1).AddMinutes(-1000); minute < End.AddDays(-1); minute = minute.AddMinutes(1))
        {
            _quietMinutes.Add(minute);
        }

        _market.Keep([new ScannerPair(Btc, true)]);

        await RunAsync(new ScannerSettings { HistoryDays = 2 });

        _market.Find(Btc)!.HistoryFrom.ShouldBe(End.AddDays(-2));
        _requests.ShouldContain("candles spot BTCUSDT 2024-03-08T12:00 2024-03-08T19:20");
    }

    // Scenario "Повторный запуск".
    [Test]
    public async Task Start_after_a_whole_load_asks_the_exchange_nothing()
    {
        _market.Keep([new ScannerPair(Sol, true, End.AddDays(-90), End, End, End)]);
        _openInterest.GetAsync(Sol, End.AddDays(-1).AddMinutes(-5), End, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OpenInterestPoint>>([.. MarketFixtures.OpenInterest().Where(point => point.Time < End)]));

        await RunAsync(new ScannerSettings());
        _market.OpenInterestPoint(Sol, new OpenInterestPoint(End, 2000m));

        _requests.ShouldBeEmpty();
        PairMetrics metrics = _market.Snapshot(End, End).Single().Metrics;
        metrics.Change24h.ShouldBe(0m);
        metrics.OpenInterestChange1h.ShouldBe(25m);
    }

    // Requirement "Пропуск после перерыва": only the minutes since the coverage are asked, and the ones the exchange leaves out had no trades.
    [Test]
    public async Task Start_after_a_break_loads_only_the_gap_and_stores_only_the_minutes_the_exchange_has()
    {
        _quietMinutes.Add(End.AddMinutes(-2));
        _market.Keep([new ScannerPair(Btc, true, End.AddDays(-90), End.AddMinutes(-3))]);
        ScannerBackfill backfill = Backfill(new ScannerSettings());

        await backfill.CatchUpAsync(Btc, CancellationToken.None);
        await backfill.CatchUpAsync(Btc, CancellationToken.None);

        _requests.ShouldBe(["candles spot BTCUSDT 2024-03-10T11:57 2024-03-10T12:00"]);
        await _candles.Received(1).UpsertAsync(
            Btc,
            Interval.OneMinute,
            Arg.Is<IReadOnlyCollection<Candle>>(stored => stored!.Select(candle => candle.OpenTime).SequenceEqual(new[] { End.AddMinutes(-3), End.AddMinutes(-1) })),
            Arg.Any<CancellationToken>());
        _market.Find(Btc)!.CoveredUntil.ShouldBe(End);
    }

    // Bybit gives a closed minute a few seconds after its end: the gap stops before a minute that closed less than that ago.
    [Test]
    public async Task Gap_stops_before_a_minute_that_closed_within_the_close_margin()
    {
        _time = new FakeTimeProvider(End.AddSeconds(3));
        _market.Keep([new ScannerPair(Btc, true, End.AddDays(-90), End.AddMinutes(-3))]);

        await Backfill(new ScannerSettings()).CatchUpAsync(Btc, CancellationToken.None);

        _requests.ShouldBe(["candles spot BTCUSDT 2024-03-10T11:57 2024-03-10T11:59"]);
        _market.Find(Btc)!.CoveredUntil.ShouldBe(End.AddMinutes(-1));
    }

    [Test]
    public async Task Funding_is_topped_up_from_where_it_ends()
    {
        _market.Keep([new ScannerPair(Sol, true, End.AddDays(-90), End, End, End.AddHours(-8)), new ScannerPair(Btc, true, End.AddDays(-90), End)]);

        await Backfill(new ScannerSettings()).TopUpFundingAsync(CancellationToken.None);

        _requests.ShouldBe(["funding SOLUSDT 2024-03-10T04:00 2024-03-10T12:00"]);
        _market.Find(Sol)!.FundingCoveredUntil.ShouldBe(End);
    }

    // Decision 4 of add-market-idea-features: a settlement topped up goes into memory at once, the last one of the pair.
    [Test]
    public async Task Funding_topped_up_goes_into_memory()
    {
        _market.Keep([new ScannerPair(Sol, true, End.AddDays(-90), End, End, End.AddHours(-8))]);
        _feed.GetFundingHistoryAsync("SOLUSDT", Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new FundingRatePoint(End.AddHours(-8), 0.0001m), new FundingRatePoint(End, 0.0006m)]);

        await Backfill(new ScannerSettings()).TopUpFundingAsync(CancellationToken.None);

        _market.FundingOf(Sol).ShouldBe(new FundingRatePoint(End, 0.0006m));
    }

    private async Task RunAsync(ScannerSettings settings)
    {
        ScannerBackfill backfill = Backfill(settings);

        while (await backfill.StepAsync(CancellationToken.None))
        {
        }
    }

    private ScannerBackfill Backfill(ScannerSettings settings)
    {
        return new ScannerBackfill(_feed, _candles, _openInterest, _funding, _pairs, _state, _market, settings, _time);
    }

    private IReadOnlyList<Candle> Candles(Instrument instrument, DateTimeOffset from, DateTimeOffset to)
    {
        _requests.Add($"candles {(instrument.Category == MarketCategory.Spot ? "spot" : "linear")} {instrument.Symbol} {Text(from)} {Text(to)}");

        // At most 1000 newest minutes, like Bybit.
        return [.. Minutes(from, to).TakeLast(1000).Select(Minute)];
    }

    private IEnumerable<MarketMinute> Minutes(DateTimeOffset from, DateTimeOffset to)
    {
        for (DateTimeOffset time = from; time < to; time = time.AddMinutes(1))
        {
            if (time >= _listedSince && !_quietMinutes.Contains(time))
            {
                yield return new MarketMinute(time, 100m, 100m, 100m, 100m, 1m);
            }
        }
    }

    private IReadOnlyList<OpenInterestPoint> Points(string symbol, DateTimeOffset from, DateTimeOffset to)
    {
        _requests.Add($"open-interest {symbol} {Text(from)} {Text(to)}");

        return [];
    }

    private IReadOnlyList<FundingRatePoint> Rates(string symbol, DateTimeOffset from, DateTimeOffset to)
    {
        _requests.Add($"funding {symbol} {Text(from)} {Text(to)}");

        return [];
    }

    private static Candle Minute(MarketMinute minute)
    {
        return new Candle(minute.OpenTime, new Price(minute.Open), new Price(minute.Open), new Price(minute.Close), new Price(minute.Close), new Volume(1m), new Volume(minute.Turnover));
    }

    private static string Text(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
    }
}