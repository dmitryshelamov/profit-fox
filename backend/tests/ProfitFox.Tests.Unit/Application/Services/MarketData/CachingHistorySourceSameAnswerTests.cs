using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Application.Services.MarketData;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Application.Services.MarketData;

/// <summary>
///     Change add-backtest-history-cache, task 2.4: a read through the cache gives what the service gives for the window —
///     spec market-history-cache «Тот же ответ, что у сервиса данных».
/// </summary>
public sealed class CachingHistorySourceSameAnswerTests
{
    private const string Btc = "btc";
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);
    private static readonly DateTimeOffset Today = Day(2026, 11, 25);
    private static readonly TimeSpan[] Intervals = [.. new[] { 1, 5, 15, 60, 240, 1440 }.Select(minutes => TimeSpan.FromMinutes(minutes))];

    [Test]
    public async Task A_window_of_months_of_the_cache_and_of_the_service_is_one_covered_range_without_a_gap_or_a_repeat()
    {
        var source = new CoveredHistorySource();
        var caching = new CachingHistorySource(source, new MemoryHistoryMonthStore(), new FakeTimeProvider(Today.AddHours(9)), NullLogger.Instance);
        await caching.ReadCandles(Btc, FourHours, Day(2026, 9, 1), Day(2026, 11, 1), CancellationToken.None);

        HistoryRead<HistoryCandle> read = await caching.ReadCandles(Btc, FourHours, Day(2026, 9, 1), Today, CancellationToken.None);

        Assert.That(source.Reads[^1], Is.EqualTo((CoveredHistorySource.Kind(FourHours), Day(2026, 11, 1), Today)), "September and October from the cache");
        Assert.That(read.Coverage, Is.EqualTo(new[] { new TimeRange(Day(2026, 9, 1), Today) }));
        Assert.That(read.Items[0].OpenTime, Is.EqualTo(Day(2026, 9, 1)));
        Assert.That(read.Items.Zip(read.Items.Skip(1)).Select(pair => pair.Second.OpenTime - pair.First.OpenTime), Has.All.EqualTo(FourHours));
        Assert.That(read.Items[^1].OpenTime, Is.EqualTo(Today - FourHours));
    }

    [Test]
    public async Task A_window_inside_a_month_of_the_cache_gives_its_candles_and_its_coverage_only()
    {
        var source = new CoveredHistorySource();
        var caching = new CachingHistorySource(source, new MemoryHistoryMonthStore(), new FakeTimeProvider(Today.AddHours(9)), NullLogger.Instance);
        await caching.ReadCandles(Btc, TimeSpan.FromMinutes(1), Day(2026, 3, 1), Day(2026, 4, 1), CancellationToken.None);
        source.Reads.Clear();
        DateTimeOffset from = Day(2026, 3, 9).AddHours(12);

        HistoryRead<HistoryCandle> read = await caching.ReadCandles(Btc, TimeSpan.FromMinutes(1), from, Day(2026, 3, 10), CancellationToken.None);

        Assert.That(source.Reads, Is.Empty);
        Assert.That(read.Coverage, Is.EqualTo(new[] { new TimeRange(from, Day(2026, 3, 10)) }));
        Assert.That(read.Items.Select(candle => candle.OpenTime), Is.EqualTo(Enumerable.Range(0, 12 * 60).Select(minute => from.AddMinutes(minute))));
    }

    /// <summary>
    ///     On random coverages with holes, random windows and a random content of the cache, the reads of
    ///     <see cref="MarketHistory" /> through the cache and their hash are those without it.
    /// </summary>
    [Test]
    public async Task Reads_through_the_cache_and_their_hash_are_those_without_it([Range(1, 24)] int seed)
    {
        var random = new Random(seed);
        DateTimeOffset now = Day(2026, 1, 1).AddDays(random.Next(0, 330)).AddMinutes(random.Next(0, 24 * 60));
        DateTimeOffset today = HistoryPeriod.Today(now);
        CoveredHistorySource source = RandomHistory(random, today);
        var direct = new CoveredHistorySource { Covered = source.Covered };
        var store = new MemoryHistoryMonthStore();
        await Prefill(random, store, direct, today);
        direct.Reads.Clear();
        var time = new FakeTimeProvider(now);
        var caching = new CachingHistorySource(source, store, time, NullLogger.Instance);
        using var cachedHash = new HistoryHash();
        using var directHash = new HistoryHash();
        var cached = new MarketHistory(caching, today, cachedHash, time);
        var plain = new MarketHistory(direct, today, directHash, time);

        for (int read = 0; read < 12; read++)
        {
            int kind = random.Next(Intervals.Length + 2);
            (DateTimeOffset from, DateTimeOffset to) = RandomWindow(random, kind < Intervals.Length ? Intervals[kind] : TimeSpan.FromDays(30), today);
            string what = $"read {read} ({kind}) of {from:O} — {to:O} on {now:O}";
            if (kind < Intervals.Length)
            {
                HistoryRead<HistoryCandle> one = await cached.Candles(Btc, Intervals[kind], from, to, CancellationToken.None);
                HistoryRead<HistoryCandle> other = await plain.Candles(Btc, Intervals[kind], from, to, CancellationToken.None);
                Assert.That(one.Coverage, Is.EqualTo(other.Coverage), what);
                Assert.That(one.Items.SequenceEqual(other.Items), Is.True, what);
            }
            else
            {
                HistoryRead<HistoryPayment> one = kind == Intervals.Length
                    ? await cached.Funding(Btc, from, to, CancellationToken.None)
                    : await cached.FundingThrough(Btc, from, to, CancellationToken.None);
                HistoryRead<HistoryPayment> other = kind == Intervals.Length
                    ? await plain.Funding(Btc, from, to, CancellationToken.None)
                    : await plain.FundingThrough(Btc, from, to, CancellationToken.None);
                Assert.That(one.Coverage, Is.EqualTo(other.Coverage), what);
                Assert.That(one.Items, Is.EqualTo(other.Items), what);
            }

            if (random.Next(4) == 0)
            {
                time.Advance(TimeSpan.FromHours(random.Next(1, 30)));
            }
        }

        Assert.That(cachedHash.Result(), Is.EqualTo(directHash.Result()));
    }

    // History listed between 2023 and the middle of 2026, with up to three holes of whole days.
    private static CoveredHistorySource RandomHistory(Random random, DateTimeOffset today)
    {
        DateTimeOffset listed = Day(2023, 1, 1).AddDays(random.Next(-20, 1200));
        List<TimeRange> holes = [];
        for (int hole = random.Next(0, 4); hole > 0; hole--)
        {
            DateTimeOffset start = listed.AddDays(random.Next(1, Math.Max(2, (int)(today - listed).TotalDays)));
            holes.Add(new TimeRange(start, start.AddDays(random.Next(1, 40))));
        }

        return CoveredHistorySource.From(listed, [.. Coverage.Merge(holes)]);
    }

    // Some of the closed months the service covers whole, saved as the cache saves them.
    private static async Task Prefill(Random random, MemoryHistoryMonthStore store, CoveredHistorySource direct, DateTimeOffset today)
    {
        for (DateTimeOffset month = Day(2023, 1, 1); month.AddMonths(1) <= today; month = month.AddMonths(1))
        {
            if (random.Next(3) != 0 || !Coverage.Covers(direct.Covered, month, month.AddMonths(1)))
            {
                continue;
            }

            int kind = random.Next(Intervals.Length + 1);
            TimeSpan? interval = kind < Intervals.Length ? Intervals[kind] : null;
            var key = new HistoryMonth(Btc, interval, month);
            if (interval is { } candles)
            {
                await store.SaveCandles(key, await direct.ReadCandles(Btc, candles, month, month.AddMonths(1), CancellationToken.None), CancellationToken.None);
            }
            else
            {
                await store.SaveFunding(key, await direct.ReadFunding(Btc, month, month.AddMonths(1), CancellationToken.None), CancellationToken.None);
            }
        }
    }

    // A window of whole days before today; short candles keep within weeks to keep the test quick.
    private static (DateTimeOffset From, DateTimeOffset To) RandomWindow(Random random, TimeSpan interval, DateTimeOffset today)
    {
        int span = interval < TimeSpan.FromMinutes(15) ? 21 : interval < TimeSpan.FromHours(1) ? 90 : (int)(today - HistoryPeriod.DataStart).TotalDays;
        int days = random.Next(1, span + 1);
        DateTimeOffset to = today.AddDays(-random.Next(0, Math.Max(1, (int)(today - HistoryPeriod.DataStart).TotalDays - days)));
        DateTimeOffset from = to.AddDays(-days);
        return (from < HistoryPeriod.DataStart ? HistoryPeriod.DataStart : from, to);
    }

    private static DateTimeOffset Day(int year, int month, int day)
    {
        return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
    }
}
