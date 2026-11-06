using ProfitFox.Application.Scanner;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Fixtures.Market;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner;

/// <summary>Decision 9: the scanner keeps the last day of every pair in memory and computes the snapshot from it.</summary>
public class ScannerMarketTests
{
    private static readonly DateTimeOffset End = MarketFixtures.End;
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Spot, "BTCUSDT");

    [Test]
    public void Snapshot_has_a_row_of_every_listed_pair_computed_from_its_day()
    {
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Sol, true), new ScannerPair(Btc, true), new ScannerPair(new Instrument(MarketCategory.Spot, "OLDUSDT"), false)]);
        market.Loaded(Sol, End.AddDays(-1), End, Day());
        market.OpenInterestLoaded(Sol, End.AddDays(-1), End.AddMinutes(1), MarketFixtures.OpenInterest());
        market.Ticker(MarketFixtures.Ticker(MarketCategory.Linear));

        IReadOnlyList<MarketSnapshotRow> rows = market.Snapshot(End, End.AddSeconds(10));

        rows.Select(row => row.Instrument).ShouldBe([Sol, Btc], ignoreOrder: true);
        MarketSnapshotRow sol = rows.Single(row => row.Instrument == Sol);
        sol.ComputedAt.ShouldBe(End.AddSeconds(10));
        sol.Metrics.ShouldBe(new PairMetrics(100.25m, 3m, 28.75m, -17.6m, 106m, -50m, 120000m, 1500000m, 200500m, 25m, -20m, 0.0001m, 1m));
        sol.Incomplete.ShouldBeFalse();
        rows.Single(row => row.Instrument == Btc).Incomplete.ShouldBeTrue();
    }

    // Requirement "Живые минутные свечи": the minute that closed moves the coverage and reaches the snapshot.
    [Test]
    public void Closed_minute_right_after_the_coverage_keeps_the_row_complete()
    {
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Sol, true)]);
        market.Loaded(Sol, End.AddDays(-1), End.AddMinutes(-1), Day().SkipLast(1));

        market.Closed(Sol, AsCandle(MarketFixtures.Day()[^1]));
        MarketSnapshotRow row = market.Snapshot(End, End).Single();

        row.Incomplete.ShouldBeFalse();
        row.Metrics.Change5m.ShouldBe(3m);
        market.Find(Sol)!.CoveredUntil.ShouldBe(End);
    }

    // Fix after review: the list is read from a copy taken before its requests, while the stream moves the coverage; a known pair takes what
    // the list tells and keeps the coverage memory holds.
    [Test]
    public void Known_pair_takes_what_the_list_tells_and_keeps_the_coverage_that_moved_meanwhile()
    {
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Sol, true)]);
        market.Loaded(Sol, End.AddDays(-1), End.AddMinutes(-1), Day().SkipLast(1));
        ScannerPair copy = market.Find(Sol)!;
        market.Closed(Sol, AsCandle(MarketFixtures.Day()[^1]));

        market.KeepListed([copy with { Listed = false, TickSize = 0.01m, TradFi = true }]);

        market.Find(Sol).ShouldBe(copy with { CoveredUntil = End, Listed = false, TickSize = 0.01m, TradFi = true });
    }

    [Test]
    public void New_pair_of_the_list_is_added_and_every_pair_the_list_changed_is_saved()
    {
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Sol, true)]);
        market.TakeChanged();

        market.KeepListed([new ScannerPair(Sol, true, TradFi: true), new ScannerPair(Btc, true)]);

        market.TakeChanged().ShouldBe([new ScannerPair(Sol, true, TradFi: true), new ScannerPair(Btc, true)], ignoreOrder: true);
    }

    // After a restart the day before the coverage comes from storage, and the gap since it from the exchange.
    [Test]
    public void Day_read_from_storage_joins_the_gap_loaded_after_it()
    {
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Sol, true, CoveredUntil: End.AddMinutes(-30))]);

        market.Loaded(Sol, End.AddMinutes(-30).AddDays(-1), End.AddMinutes(-30), MarketFixtures.Day().Where(minute => minute.OpenTime < End.AddMinutes(-30)).Select(AsCandle));
        market.Loaded(Sol, End.AddMinutes(-30), End, MarketFixtures.Day().Where(minute => minute.OpenTime >= End.AddMinutes(-30)).Select(AsCandle));

        market.Snapshot(End, End).Single().Metrics.Change4h.ShouldBe(106m);
    }

    [Test]
    public void Gap_loaded_without_the_day_before_it_covers_only_itself()
    {
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Sol, true, CoveredUntil: End.AddMinutes(-30))]);

        market.Loaded(Sol, End.AddMinutes(-30), End, MarketFixtures.Day().Where(minute => minute.OpenTime >= End.AddMinutes(-30)).Select(AsCandle));
        PairMetrics metrics = market.Snapshot(End, End).Single().Metrics;

        metrics.Change15m.ShouldBe(28.75m);
        metrics.Change1h.ShouldBeNull();
    }

    [Test]
    public void Lagging_pairs_are_the_listed_ones_whose_coverage_ends_before_the_minute()
    {
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Sol, true, CoveredUntil: End), new ScannerPair(Btc, true, CoveredUntil: End.AddMinutes(-3)), new ScannerPair(new Instrument(MarketCategory.Spot, "OLDUSDT"), false)]);

        market.Lagging(End).ShouldBe([Btc]);
    }

    // The pairs whose coverage changed are saved once a minute in one batch.
    [Test]
    public void Changed_pairs_are_taken_once()
    {
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Sol, true, CoveredUntil: End.AddMinutes(-1)), new ScannerPair(Btc, true, CoveredUntil: End)]);
        market.TakeChanged();

        market.Closed(Sol, AsCandle(MarketFixtures.Day()[^1]));
        IReadOnlyList<ScannerPair> changed = market.TakeChanged();

        changed.ShouldBe([new ScannerPair(Sol, true, CoveredUntil: End)]);
        market.TakeChanged().ShouldBeEmpty();
    }

    [Test]
    public void Change_of_a_pair_record_is_kept_and_taken()
    {
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Sol, true)]);
        market.TakeChanged();

        market.Update(Sol, pair => pair with { HistoryFrom = End.AddDays(-90) });

        market.Find(Sol)!.HistoryFrom.ShouldBe(End.AddDays(-90));
        market.TakeChanged().ShouldBe([new ScannerPair(Sol, true, HistoryFrom: End.AddDays(-90))]);
    }

    // Memory holds the last day and the minute before it, whatever the stream brings over weeks.
    [Test]
    public void Snapshot_lets_go_of_minutes_older_than_a_day_but_the_last_of_them()
    {
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Sol, true)]);
        market.Loaded(Sol, End.AddDays(-1), End, Day());

        market.Snapshot(End.AddMinutes(10), End.AddMinutes(10));

        IReadOnlyList<MarketMinute> kept = market.Minutes(Sol);
        kept[0].OpenTime.ShouldBe(End.AddMinutes(10).AddDays(-1).AddMinutes(-1));
        kept.Count.ShouldBe(1440 - 10 + 1);
    }

    [Test]
    public void Snapshot_lets_go_of_points_of_open_interest_older_than_the_day_and_a_step()
    {
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Sol, true)]);
        market.OpenInterestLoaded(Sol, End.AddDays(-1), End.AddMinutes(1), MarketFixtures.OpenInterest());

        market.Snapshot(End.AddMinutes(10), End.AddMinutes(10));

        market.OpenInterest(Sol)[0].Time.ShouldBe(End.AddMinutes(10).AddDays(-1).AddMinutes(-5));
    }

    // A new record of a known pair, such as from the list read again, keeps what memory holds of it.
    [Test]
    public void New_record_of_a_known_pair_keeps_its_day()
    {
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Sol, true)]);
        market.Loaded(Sol, End.AddDays(-1), End, Day());

        market.Keep([new ScannerPair(Sol, true, CoveredUntil: End)]);

        market.Snapshot(End, End).Single().Metrics.Change24h.ShouldBe(-50m);
    }

    // Tickers come for every instrument of a category, and the stream may still bring a pair the scanner let go of.
    [Test]
    public void Data_of_a_pair_the_scanner_does_not_keep_is_left_out()
    {
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Sol, true)]);

        market.Ticker(MarketFixtures.Ticker(MarketCategory.Spot));
        market.Closed(Btc, AsCandle(MarketFixtures.Day()[^1]));

        market.Find(Btc).ShouldBeNull();
        market.Snapshot(End, End).Select(row => row.Instrument).ShouldBe([Sol]);
    }

    private static IEnumerable<Candle> Day()
    {
        return MarketFixtures.Day().Select(AsCandle);
    }

    // Change 2: the ideas measure minutes by their high and low.
    [Test]
    public void Minutes_keep_their_high_and_low()
    {
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Sol, true, CoveredUntil: End)]);

        market.Closed(Sol, new Candle(End, new Price(100m), new Price(102m), new Price(99m), new Price(101m), new Volume(3m), new Volume(300m)));

        market.Minutes(Sol).ShouldBe([new MarketMinute(End, 100m, 102m, 99m, 101m, 300m)]);
    }

    private static Candle AsCandle(MarketMinute minute)
    {
        return new Candle(minute.OpenTime, new Price(minute.Open), new Price(minute.Open), new Price(minute.Close), new Price(minute.Close), new Volume(1m), new Volume(minute.Turnover));
    }
}