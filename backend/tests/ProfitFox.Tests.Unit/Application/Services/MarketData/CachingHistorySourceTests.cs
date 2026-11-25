using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Services.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Logging;

namespace ProfitFox.Tests.Unit.Application.Services.MarketData;

/// <summary>
///     Change add-backtest-history-cache, tasks 2.1–2.3, 2.5 and 2.6: the rules of the cache of history on a store in memory —
///     spec market-history-cache «Месяц — единица хранения», «Что не сохраняется», failures and counters.
/// </summary>
public sealed class CachingHistorySourceTests
{
    private const string Btc = "btc";
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);
    private static readonly DateTimeOffset Today = Day(2026, 11, 25);
    private static readonly DateTimeOffset March = Day(2026, 3, 1);
    private static readonly DateTimeOffset April = Day(2026, 4, 1);

    // Task 2.1.

    [Test]
    public async Task A_week_of_minutes_fills_the_month()
    {
        var source = new CoveredHistorySource();
        var store = new MemoryHistoryMonthStore();

        HistoryRead<HistoryCandle> read = await Caching(source, store).ReadCandles(Btc, Minute, Day(2026, 3, 9), Day(2026, 3, 16), CancellationToken.None);

        Assert.That(source.Reads, Is.EqualTo(new[] { (CoveredHistorySource.Kind(Minute), March, April) }));
        Assert.That(store.Months, Is.EqualTo(new[] { new HistoryMonth(Btc, Minute, March) }));
        await AssertSame(source, read, Btc, Minute, Day(2026, 3, 9), Day(2026, 3, 16));
        Assert.That(read.Items, Has.Count.EqualTo(7 * 24 * 60), "only the candles of the window");
    }

    [Test]
    public async Task A_second_week_of_the_same_month_asks_the_service_nothing()
    {
        var source = new CoveredHistorySource();
        CachingHistorySource caching = Caching(source, new MemoryHistoryMonthStore());
        await caching.ReadCandles(Btc, Minute, Day(2026, 3, 9), Day(2026, 3, 16), CancellationToken.None);
        source.Reads.Clear();

        HistoryRead<HistoryCandle> read = await caching.ReadCandles(Btc, Minute, Day(2026, 3, 16), Day(2026, 3, 23), CancellationToken.None);

        Assert.That(source.Reads, Is.Empty);
        await AssertSame(source, read, Btc, Minute, Day(2026, 3, 16), Day(2026, 3, 23));
    }

    [Test]
    public async Task Candles_of_another_interval_are_read_and_kept_apart()
    {
        var source = new CoveredHistorySource();
        var store = new MemoryHistoryMonthStore();
        CachingHistorySource caching = Caching(source, store);
        await caching.ReadCandles(Btc, FourHours, March, April, CancellationToken.None);
        source.Reads.Clear();

        HistoryRead<HistoryCandle> read = await caching.ReadCandles(Btc, Hour, March, April, CancellationToken.None);

        Assert.That(source.Reads, Is.EqualTo(new[] { (CoveredHistorySource.Kind(Hour), March, April) }));
        Assert.That(store.Months, Is.EquivalentTo(new[] { new HistoryMonth(Btc, FourHours, March), new HistoryMonth(Btc, Hour, March) }));
        await AssertSame(source, read, Btc, Hour, March, April);
    }

    [Test]
    public async Task Funding_of_a_part_of_a_month_fills_the_month()
    {
        var source = new CoveredHistorySource();
        var store = new MemoryHistoryMonthStore();
        CachingHistorySource caching = Caching(source, store);

        HistoryRead<HistoryPayment> first = await caching.ReadFunding(Btc, Day(2026, 3, 9), Day(2026, 3, 16).AddSeconds(1), CancellationToken.None);
        HistoryRead<HistoryPayment> second = await caching.ReadFunding(Btc, Day(2026, 3, 16), Day(2026, 3, 23).AddSeconds(1), CancellationToken.None);

        Assert.That(source.Reads, Is.EqualTo(new[] { ("funding", March, April) }), "the second read is the cache's");
        Assert.That(store.Months, Is.EqualTo(new[] { new HistoryMonth(Btc, null, March) }));
        await AssertSameFunding(source, first, Btc, Day(2026, 3, 9), Day(2026, 3, 16).AddSeconds(1));
        await AssertSameFunding(source, second, Btc, Day(2026, 3, 16), Day(2026, 3, 23).AddSeconds(1));
        Assert.That(second.Items[^1].Time, Is.EqualTo(Day(2026, 3, 23)), "the payment at the end is told");
    }

    // Task 2.2.

    [Test]
    public async Task A_month_with_a_hole_is_not_kept_and_both_reads_go_to_the_service()
    {
        var hole = new TimeRange(Day(2026, 3, 10), Day(2026, 3, 12));
        var source = CoveredHistorySource.From(Day(2025, 1, 1), hole);
        var store = new MemoryHistoryMonthStore();
        CachingHistorySource caching = Caching(source, store);

        HistoryRead<HistoryCandle> first = await caching.ReadCandles(Btc, FourHours, March, April, CancellationToken.None);
        HistoryRead<HistoryCandle> second = await caching.ReadCandles(Btc, FourHours, March, April, CancellationToken.None);

        Assert.That(store.Months, Is.Empty);
        Assert.That(source.Reads, Has.Count.EqualTo(2));
        Assert.That(second.Coverage, Is.EqualTo(new[] { new TimeRange(March, hole.From), new TimeRange(hole.Until, April) }));
        Assert.That(first.Coverage, Is.EqualTo(second.Coverage));
    }

    [Test]
    public async Task A_second_read_of_a_month_known_as_not_whole_asks_the_service_the_window_only()
    {
        var source = CoveredHistorySource.From(Day(2025, 1, 1), new TimeRange(Day(2026, 3, 10), Day(2026, 3, 12)));
        CachingHistorySource caching = Caching(source, new MemoryHistoryMonthStore());
        await caching.ReadCandles(Btc, Minute, Day(2026, 3, 2), Day(2026, 3, 9), CancellationToken.None);

        HistoryRead<HistoryCandle> read = await caching.ReadCandles(Btc, Minute, Day(2026, 3, 16), Day(2026, 3, 23), CancellationToken.None);

        Assert.That(source.Reads, Is.EqualTo(new[]
        {
            (CoveredHistorySource.Kind(Minute), March, April),
            (CoveredHistorySource.Kind(Minute), Day(2026, 3, 16), Day(2026, 3, 23))
        }));
        await AssertSame(source, read, Btc, Minute, Day(2026, 3, 16), Day(2026, 3, 23));
    }

    [Test]
    public async Task The_month_of_the_listing_and_the_months_before_it_are_not_kept()
    {
        var source = CoveredHistorySource.From(new DateTimeOffset(2025, 6, 17, 0, 0, 0, TimeSpan.Zero));
        var store = new MemoryHistoryMonthStore();

        HistoryRead<HistoryCandle> read = await Caching(source, store).ReadCandles(Btc, FourHours, HistoryPeriod.DataStart, Today, CancellationToken.None);

        DateTimeOffset[] kept = [.. Enumerable.Range(0, 16).Select(month => Day(2025, 7, 1).AddMonths(month))];
        Assert.That(store.Months.Select(month => month.Start).Order(), Is.EqualTo(kept), "July 2025 to October 2026");
        Assert.That(store.Months.Select(month => month.Interval), Has.All.EqualTo(FourHours));
        await AssertSame(source, read, Btc, FourHours, HistoryPeriod.DataStart, Today);
    }

    [Test]
    public async Task A_window_up_to_the_current_day_keeps_the_closed_month_and_reads_the_open_one_each_time()
    {
        var source = new CoveredHistorySource();
        var store = new MemoryHistoryMonthStore();
        CachingHistorySource caching = Caching(source, store, new FakeTimeProvider(Today.AddHours(15.5)));
        DateTimeOffset end = Today.AddHours(15);

        HistoryRead<HistoryCandle> first = await caching.ReadCandles(Btc, Hour, Day(2026, 10, 1), end, CancellationToken.None);
        HistoryRead<HistoryCandle> second = await caching.ReadCandles(Btc, Hour, Day(2026, 10, 1), end, CancellationToken.None);

        Assert.That(store.Months, Is.EqualTo(new[] { new HistoryMonth(Btc, Hour, Day(2026, 10, 1)) }), "October, not November");
        Assert.That(source.Reads, Is.EqualTo(new[]
        {
            (CoveredHistorySource.Kind(Hour), Day(2026, 10, 1), end),
            (CoveredHistorySource.Kind(Hour), Day(2026, 11, 1), end)
        }));
        await AssertSame(source, first, Btc, Hour, Day(2026, 10, 1), end);
        Assert.That(second.Coverage, Is.EqualTo(first.Coverage));
        Assert.That(second.Items.SequenceEqual(first.Items), Is.True);
    }

    [Test]
    public async Task On_the_first_day_of_a_month_the_month_before_is_kept()
    {
        var store = new MemoryHistoryMonthStore();
        var time = new FakeTimeProvider(Day(2026, 12, 1).AddMinutes(10));

        await Caching(new CoveredHistorySource(), store, time).ReadCandles(Btc, FourHours, Day(2026, 11, 20), Day(2026, 12, 1), CancellationToken.None);

        Assert.That(store.Months, Is.EqualTo(new[] { new HistoryMonth(Btc, FourHours, Day(2026, 11, 1)) }));
    }

    [Test]
    public async Task The_current_day_is_that_of_the_moment_of_the_read()
    {
        var source = new CoveredHistorySource();
        var store = new MemoryHistoryMonthStore();
        var time = new FakeTimeProvider(Day(2026, 11, 30).AddHours(23.5));
        CachingHistorySource caching = Caching(source, store, time);

        await caching.ReadCandles(Btc, FourHours, Day(2026, 11, 20), Day(2026, 11, 30), CancellationToken.None);
        Assert.That(store.Months, Is.Empty, "November is open before midnight");
        time.Advance(TimeSpan.FromHours(1));
        await caching.ReadCandles(Btc, FourHours, Day(2026, 11, 20), Day(2026, 11, 30), CancellationToken.None);

        Assert.That(store.Months, Is.EqualTo(new[] { new HistoryMonth(Btc, FourHours, Day(2026, 11, 1)) }));
        Assert.That(source.Reads[^1], Is.EqualTo((CoveredHistorySource.Kind(FourHours), Day(2026, 11, 1), Day(2026, 12, 1))));
    }

    // Task 2.3.

    [Test]
    public async Task The_first_read_of_the_candles_of_a_timeframe_is_one_request()
    {
        var source = CoveredHistorySource.From(new DateTimeOffset(2025, 6, 17, 0, 0, 0, TimeSpan.Zero));

        await Caching(source, new MemoryHistoryMonthStore()).ReadCandles(Btc, FourHours, HistoryPeriod.DataStart, Today, CancellationToken.None);

        Assert.That(source.Reads, Is.EqualTo(new[] { (CoveredHistorySource.Kind(FourHours), HistoryPeriod.DataStart, Today) }));
    }

    [Test]
    public async Task A_read_with_the_middle_of_its_window_in_the_cache_is_two_requests()
    {
        var source = new CoveredHistorySource();
        CachingHistorySource caching = Caching(source, new MemoryHistoryMonthStore());
        await caching.ReadCandles(Btc, FourHours, Day(2025, 3, 1), Day(2025, 5, 1), CancellationToken.None);
        source.Reads.Clear();

        HistoryRead<HistoryCandle> read = await caching.ReadCandles(Btc, FourHours, HistoryPeriod.DataStart, Today, CancellationToken.None);

        Assert.That(source.Reads, Is.EqualTo(new[]
        {
            (CoveredHistorySource.Kind(FourHours), HistoryPeriod.DataStart, Day(2025, 3, 1)),
            (CoveredHistorySource.Kind(FourHours), Day(2025, 5, 1), Today)
        }));
        await AssertSame(source, read, Btc, FourHours, HistoryPeriod.DataStart, Today);
    }

    // Task 2.5.

    [Test]
    public void An_error_of_the_service_goes_on_and_nothing_of_the_read_is_kept()
    {
        var source = new CoveredHistorySource();
        source.Errors.Enqueue(new HistoryUnavailableException("dropped", dropped: true));
        var store = new MemoryHistoryMonthStore();

        HistoryUnavailableException? error = Assert.ThrowsAsync<HistoryUnavailableException>(
            () => Caching(source, store).ReadCandles(Btc, FourHours, Day(2026, 1, 1), Day(2026, 5, 1), CancellationToken.None));

        Assert.That(error!.Dropped, Is.True);
        Assert.That(store.Months, Is.Empty);
    }

    [Test]
    public async Task An_error_of_the_service_after_a_first_request_keeps_the_months_of_that_request_only()
    {
        var source = new CoveredHistorySource();
        var store = new MemoryHistoryMonthStore();
        CachingHistorySource caching = Caching(source, store);
        await caching.ReadCandles(Btc, FourHours, Day(2026, 3, 1), Day(2026, 4, 1), CancellationToken.None);
        source.AfterRead = () => source.Errors.Enqueue(new HistoryUnavailableException("dropped", dropped: true));

        Assert.ThrowsAsync<HistoryUnavailableException>(() => caching.ReadCandles(Btc, FourHours, Day(2026, 1, 1), Day(2026, 6, 1), CancellationToken.None));

        Assert.That(store.Months.Select(month => month.Start).Order(), Is.EqualTo(new[] { Day(2026, 1, 1), Day(2026, 2, 1), Day(2026, 3, 1) }));
    }

    [Test]
    public async Task A_month_the_store_cannot_read_is_read_from_the_service()
    {
        var source = new CoveredHistorySource();
        var store = new MemoryHistoryMonthStore();
        CachingHistorySource caching = Caching(source, store);
        await caching.ReadCandles(Btc, FourHours, March, April, CancellationToken.None);
        store.FailReads = true;
        source.Reads.Clear();

        HistoryRead<HistoryCandle> read = await caching.ReadCandles(Btc, FourHours, March, April, CancellationToken.None);

        Assert.That(source.Reads, Is.EqualTo(new[] { (CoveredHistorySource.Kind(FourHours), March, April) }));
        await AssertSame(source, read, Btc, FourHours, March, April);
    }

    [Test]
    public async Task A_month_the_store_cannot_save_gives_the_answer_of_the_service_and_a_warning_once_a_minute()
    {
        var source = new CoveredHistorySource();
        var store = new MemoryHistoryMonthStore { FailSaves = true };
        var time = new FakeTimeProvider(Today.AddHours(12));
        var logger = new ListLogger();
        var caching = new CachingHistorySource(source, store, time, logger);

        HistoryRead<HistoryCandle> read = await caching.ReadCandles(Btc, FourHours, Day(2026, 1, 1), Day(2026, 5, 1), CancellationToken.None);
        await caching.ReadFunding(Btc, Day(2026, 1, 1), Day(2026, 5, 1), CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(59));
        await caching.ReadCandles(Btc, FourHours, Day(2026, 1, 1), Day(2026, 5, 1), CancellationToken.None);
        Assert.That(Warnings(logger), Is.EqualTo(1), "one warning within a minute");
        time.Advance(TimeSpan.FromSeconds(2));
        await caching.ReadCandles(Btc, FourHours, Day(2026, 1, 1), Day(2026, 5, 1), CancellationToken.None);

        Assert.That(Warnings(logger), Is.EqualTo(2));
        Assert.That(logger.Entries[0].Message, Does.Contain("cache of history"));
        Assert.That(store.Months, Is.Empty);
        await AssertSame(source, read, Btc, FourHours, Day(2026, 1, 1), Day(2026, 5, 1));
    }

    [Test]
    public void A_cancelled_read_keeps_nothing()
    {
        var source = new CoveredHistorySource();
        var store = new MemoryHistoryMonthStore();
        using var cancellation = new CancellationTokenSource();
        source.AfterRead = cancellation.Cancel;

        Assert.That(
            () => Caching(source, store).ReadCandles(Btc, FourHours, Day(2026, 1, 1), Day(2026, 5, 1), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(store.Months, Is.Empty);
    }

    // Task 2.6.

    [Test]
    public async Task The_counters_tell_the_months_from_the_cache_the_months_saved_and_the_requests()
    {
        var source = new CoveredHistorySource();
        CachingHistorySource caching = Caching(source, new MemoryHistoryMonthStore());
        Assert.That(caching.Counts, Is.EqualTo(HistoryCacheCounts.Zero));

        await caching.ReadCandles(Btc, FourHours, Day(2026, 2, 1), Day(2026, 5, 1), CancellationToken.None);
        await caching.ReadFunding(Btc, Day(2026, 2, 1), Day(2026, 5, 1), CancellationToken.None);
        Assert.That(caching.Counts, Is.EqualTo(new HistoryCacheCounts(FromCache: 0, Saved: 6, Requests: 2)));
        await caching.ReadCandles(Btc, FourHours, Day(2026, 2, 1), Day(2026, 5, 1), CancellationToken.None);
        await caching.ReadFunding(Btc, Day(2026, 2, 1), Day(2026, 5, 1), CancellationToken.None);
        await caching.ReadCandles(Btc, FourHours, Day(2026, 4, 1), Today, CancellationToken.None);

        Assert.That(caching.Counts, Is.EqualTo(new HistoryCacheCounts(FromCache: 7, Saved: 12, Requests: 3)), "May to October saved, November asked");
        Assert.That(caching.Counts - new HistoryCacheCounts(6, 6, 2), Is.EqualTo(new HistoryCacheCounts(1, 6, 1)));
    }

    private static int Warnings(ListLogger logger)
    {
        return logger.Entries.Count(entry => entry.Level == LogLevel.Warning);
    }

    private static CachingHistorySource Caching(IHistorySource source, IHistoryMonthStore store, TimeProvider? time = null)
    {
        return new CachingHistorySource(source, store, time ?? new FakeTimeProvider(Today.AddHours(12)), NullLogger.Instance);
    }

    private static DateTimeOffset Day(int year, int month, int day)
    {
        return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
    }

    // The read is the one the service gives for the window.
    private static async Task AssertSame(CoveredHistorySource source, HistoryRead<HistoryCandle> read, string instrument, TimeSpan interval, DateTimeOffset from, DateTimeOffset to)
    {
        HistoryRead<HistoryCandle> direct = await new CoveredHistorySource { Covered = source.Covered }.ReadCandles(instrument, interval, from, to, CancellationToken.None);
        Assert.That(read.Coverage, Is.EqualTo(direct.Coverage), "the coverage the service gives for the window");
        Assert.That(read.Items.SequenceEqual(direct.Items), Is.True, "the candles the service gives for the window, in its order");
    }

    private static async Task AssertSameFunding(CoveredHistorySource source, HistoryRead<HistoryPayment> read, string instrument, DateTimeOffset from, DateTimeOffset to)
    {
        HistoryRead<HistoryPayment> direct = await new CoveredHistorySource { Covered = source.Covered }.ReadFunding(instrument, from, to, CancellationToken.None);
        Assert.That(read.Coverage, Is.EqualTo(direct.Coverage), "the coverage the service gives for the window");
        Assert.That(read.Items, Is.EqualTo(direct.Items), "the payments the service gives for the window, in its order");
    }
}
