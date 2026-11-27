using System.IO.Compression;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Services.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Infrastructure.Persistence.Files.HistoryCache;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Logging;

using static ProfitFox.Tests.Integration.Infrastructure.Persistence.Files.HistoryCache.HistoryCacheFolder;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Files.HistoryCache;

/// <summary>
///     Change add-backtest-history-cache, tasks 3.1–3.5: months of history in files of a temporary folder — spec
///     market-history-cache «Сбой файла или папки кэша», «Предел размера кэша» and «Одна папка у процессов одной машины».
/// </summary>
[NonParallelizable]
public sealed class FileHistoryMonthStoreTests
{
    private const long Megabyte = 1024 * 1024;
    private const long Unlimited = 1L << 40;
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);
    private static readonly HistoryMonth March = new HistoryMonth("btc", Minute, Day(2026, 3, 1));
    private HistoryCacheFolder _folder = null!;
    private FakeTimeProvider _time = null!;

    [SetUp]
    public void Create()
    {
        FakeMarketData.CoveredUntil = null;
        _folder = new HistoryCacheFolder();
        // The clock of the test starts now: a disk mounted with relatime moves a read time older than the time of writing
        // by itself, and the times the store sets are to stay ahead of that.
        _time = new FakeTimeProvider(DateTimeOffset.UtcNow);
    }

    [TearDown]
    public void Remove()
    {
        _folder.Dispose();
    }

    // Task 3.1.

    [Test]
    public async Task A_month_of_candles_reads_back_as_it_was_saved()
    {
        HistoryRead<HistoryCandle> month = Minutes(March, 31 * 24 * 60);
        Assert.That(month.Items.Any(candle => candle.Turnover is null) && month.Items.Any(candle => candle.BuyVolume is null), Is.True);

        await Store().SaveCandles(March, month, CancellationToken.None);
        HistoryRead<HistoryCandle>? read = await Store().ReadCandles(March, CancellationToken.None);

        Assert.That(read, Is.Not.Null);
        Assert.That(read!.Coverage, Is.EqualTo(month.Coverage));
        Assert.That(read.Items.SequenceEqual(month.Items), Is.True, "every field, the missing ones too");
        Assert.That(_folder.Months, Is.EqualTo(new[] { System.IO.Path.Combine(_folder.Path, "btc", "candles-1m", "2026-03.gz") }));
        Assert.That(_folder.Temporaries, Is.Empty);
    }

    [Test]
    public async Task A_month_of_funding_reads_back_as_it_was_saved()
    {
        var month = new HistoryMonth("btc", null, Day(2026, 3, 1));
        HistoryRead<HistoryPayment> funding = Funding(month);

        await Store().SaveFunding(month, funding, CancellationToken.None);
        HistoryRead<HistoryPayment>? read = await Store().ReadFunding(month, CancellationToken.None);

        Assert.That(read!.Coverage, Is.EqualTo(funding.Coverage));
        Assert.That(read.Items, Is.EqualTo(funding.Items));
        Assert.That(File.Exists(System.IO.Path.Combine(_folder.Path, "btc", "funding", "2026-03.gz")), Is.True);
    }

    /// <summary>
    ///     Change add-idea-score, task 5.2 (design.md, decision 6): a month of open interest is its own kind — written with the
    ///     kind 3 in a folder of its own — and reads back as it was saved; the files of candles and funding keep the kinds 1
    ///     and 2 they had, so the files written before read as they did.
    /// </summary>
    [Test]
    public async Task A_month_of_open_interest_is_a_kind_of_its_own_and_candles_and_funding_keep_theirs()
    {
        var points = HistoryMonth.OpenInterest("btc", Day(2026, 3, 1));
        var funding = new HistoryMonth("btc", null, Day(2026, 3, 1));
        var interest = new HistoryRead<OpenInterestPoint>(
            [new TimeRange(points.Start, points.End)],
            [.. Enumerable.Range(0, 31 * 288).Select(step => new OpenInterestPoint(points.Start.AddMinutes(5 * step), 52345.123m + step))]);
        FileHistoryMonthStore store = Store();

        await store.SaveOpenInterest(points, interest, CancellationToken.None);
        await store.SaveFunding(funding, Funding(funding), CancellationToken.None);
        await store.SaveCandles(March, Minutes(March, 10), CancellationToken.None);
        HistoryRead<OpenInterestPoint>? read = await Store().ReadOpenInterest(points, CancellationToken.None);

        Assert.That(read!.Coverage, Is.EqualTo(interest.Coverage));
        Assert.That(read.Items, Is.EqualTo(interest.Items), "the time and the value with its decimals");
        Assert.That(store.PathOf(points), Is.EqualTo(System.IO.Path.Combine(_folder.Path, "btc", "open-interest", "2026-03.gz")));
        Assert.That(KindIn(store.PathOf(points)), Is.EqualTo(3));
        Assert.That(KindIn(store.PathOf(funding)), Is.EqualTo(2));
        Assert.That(KindIn(store.PathOf(March)), Is.EqualTo(1));
        Assert.That((await Store().ReadFunding(funding, CancellationToken.None))!.Items, Is.EqualTo(Funding(funding).Items));
    }

    [Test]
    public async Task A_pair_with_signs_a_folder_cannot_have_keeps_its_months_inside_the_folder_of_the_cache()
    {
        var odd = new HistoryMonth("../Bybit:Linear/BTC*USDT?\"<>|\\ é", FourHours, Day(2026, 3, 1));
        HistoryMonth lower = odd with { InstrumentId = odd.InstrumentId.ToLowerInvariant() };
        HistoryRead<HistoryCandle> month = Minutes(odd, 100);
        HistoryRead<HistoryCandle> other = Minutes(lower, 50, seed: 2);

        await Store().SaveCandles(odd, month, CancellationToken.None);
        await Store().SaveCandles(lower, other, CancellationToken.None);

        Assert.That((await Store().ReadCandles(odd, CancellationToken.None))!.Items.SequenceEqual(month.Items), Is.True);
        Assert.That((await Store().ReadCandles(lower, CancellationToken.None))!.Items.SequenceEqual(other.Items), Is.True, "ids that differ in case only keep apart");
        Assert.That(_folder.Months, Has.Length.EqualTo(2));
        Assert.That(_folder.Months.Select(path => System.IO.Path.GetRelativePath(_folder.Path, path).Split(System.IO.Path.DirectorySeparatorChar).Length), Has.All.EqualTo(3));
        Assert.That(FileHistoryMonthStore.KindName(TimeSpan.FromDays(1)), Is.EqualTo("candles-1d"));
        Assert.That(FileHistoryMonthStore.KindName(TimeSpan.FromMinutes(15)), Is.EqualTo("candles-15m"));
        Assert.That(FileHistoryMonthStore.KindName(TimeSpan.FromHours(4)), Is.EqualTo("candles-4h"));
    }

    // Task 3.2.

    [Test]
    public async Task A_file_cut_in_half_is_read_from_the_service_again_and_replaced_by_a_whole_one()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var month = new HistoryMonth("btc", FourHours, Day(2026, 3, 1));
        FileHistoryMonthStore store = Store();
        CachingHistorySource caching = Caching(server, store);
        await caching.ReadCandles("btc", FourHours, Day(2026, 3, 9), Day(2026, 3, 16), CancellationToken.None);
        string path = store.PathOf(month);
        byte[] whole = await File.ReadAllBytesAsync(path);
        await File.WriteAllBytesAsync(path, whole[..(whole.Length / 2)]);
        server.Data.CandleRequests.Clear();

        HistoryRead<HistoryCandle> read = await Caching(server, Store()).ReadCandles("btc", FourHours, Day(2026, 3, 9), Day(2026, 3, 16), CancellationToken.None);

        Assert.That(server.Data.CandleRequests.Select(request => (request.From.ToDateTimeOffset(), request.To.ToDateTimeOffset())), Is.EqualTo(new[] { (Day(2026, 3, 1), Day(2026, 4, 1)) }), "March again, whole");
        HistoryRead<HistoryCandle> direct = await new GrpcHistorySource(server.Channel).ReadCandles("btc", FourHours, Day(2026, 3, 9), Day(2026, 3, 16), CancellationToken.None);
        Assert.That(read.Coverage, Is.EqualTo(direct.Coverage));
        Assert.That(read.Items, Is.EqualTo(direct.Items));
        Assert.That(new FileInfo(path).Length, Is.EqualTo(whole.Length), "a whole file in its place");
        Assert.That(await Store().ReadCandles(month, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task A_file_left_by_a_stop_in_the_middle_of_a_write_is_not_taken_for_the_month()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var month = new HistoryMonth("btc", FourHours, Day(2026, 3, 1));
        await Store().SaveCandles(month, Minutes(month, 180), CancellationToken.None);
        string path = Store().PathOf(month);
        byte[] whole = await File.ReadAllBytesAsync(path);
        File.Delete(path);
        // A writer stopped after half of the month: its temporary file stays beside the name of the month.
        await File.WriteAllBytesAsync($"{path}.{Guid.NewGuid():N}.tmp", whole[..(whole.Length / 2)]);

        Assert.That(await Store().ReadCandles(month, CancellationToken.None), Is.Null);
        await Caching(server, Store()).ReadCandles("btc", FourHours, Day(2026, 3, 9), Day(2026, 3, 16), CancellationToken.None);

        Assert.That(server.Data.CandleRequests, Has.Count.EqualTo(1), "the month is read from the service");
        Assert.That(await Store().ReadCandles(month, CancellationToken.None), Is.Not.Null, "and kept");
    }

    [Test]
    public async Task A_file_of_random_bytes_of_another_month_or_of_another_format_is_no_month_and_is_replaced_by_the_next_save()
    {
        HistoryRead<HistoryCandle> march = Minutes(March, 300);
        HistoryMonth april = March with { Start = Day(2026, 4, 1) };
        await Store().SaveCandles(april, Minutes(april, 300), CancellationToken.None);
        string path = Store().PathOf(March);
        byte[] random = new byte[4096];
        new Random(7).NextBytes(random);
        byte[] gzipOfRandom = Compress(random);
        byte[] header = [.. FileHistoryMonthStore.Mark, .. BitConverter.GetBytes(FileHistoryMonthStore.Format + 1)];
        byte[] otherFormat = Compress([.. header, .. Decompress(await File.ReadAllBytesAsync(Store().PathOf(april)))[header.Length..]]);

        foreach ((string what, byte[] bytes) in new[] { ("random bytes", random), ("GZip of random bytes", gzipOfRandom), ("another format", otherFormat), ("April", await File.ReadAllBytesAsync(Store().PathOf(april))) })
        {
            await File.WriteAllBytesAsync(path, bytes);

            Assert.That(await Store().ReadCandles(March, CancellationToken.None), Is.Null, what);
            await Store().SaveCandles(March, march, CancellationToken.None);
            Assert.That((await Store().ReadCandles(March, CancellationToken.None))!.Items.SequenceEqual(march.Items), Is.True, what);
        }

        Assert.That(await Store().ReadFunding(March with { Interval = null }, CancellationToken.None), Is.Null, "no funding where candles are");
    }

    [Test]
    public async Task A_temporary_file_older_than_an_hour_is_removed_at_the_first_use_of_the_store()
    {
        await Store().SaveCandles(March, Minutes(March, 10), CancellationToken.None);
        string folder = System.IO.Path.GetDirectoryName(Store().PathOf(March))!;
        string old = System.IO.Path.Combine(folder, $"2026-03.gz.{Guid.NewGuid():N}.tmp");
        string young = System.IO.Path.Combine(folder, $"2026-04.gz.{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(old, [1, 2, 3]);
        await File.WriteAllBytesAsync(young, [1, 2, 3]);
        File.SetLastWriteTimeUtc(old, _time.GetUtcNow().UtcDateTime.AddMinutes(-61));
        File.SetLastWriteTimeUtc(young, _time.GetUtcNow().UtcDateTime.AddMinutes(-50));

        await Store().ReadCandles(March, CancellationToken.None);

        Assert.That(File.Exists(old), Is.False);
        Assert.That(File.Exists(young), Is.True, "a neighbour may still be writing it");
    }

    // Task 3.3.

    [Test]
    public async Task A_cache_grown_past_its_limit_removes_the_month_read_least_recently()
    {
        (HistoryMonth a, HistoryMonth b, HistoryMonth c) = Months();
        FileHistoryMonthStore store = Store(await LimitOfTwoMonths());
        await Save(store, a, b);
        _time.Advance(TimeSpan.FromHours(2));

        await store.SaveCandles(c, Noise(c, 4, seed: 3), CancellationToken.None);

        Assert.That(await store.ReadCandles(a, CancellationToken.None), Is.Null, "A, read before B, is gone");
        Assert.That(await store.ReadCandles(b, CancellationToken.None), Is.Not.Null);
        Assert.That(await store.ReadCandles(c, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task A_read_lengthens_the_life_of_a_month()
    {
        (HistoryMonth a, HistoryMonth b, HistoryMonth c) = Months();
        FileHistoryMonthStore store = Store(await LimitOfTwoMonths());
        await Save(store, a, b);
        _time.Advance(TimeSpan.FromHours(2));
        await store.ReadCandles(a, CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(2));

        await store.SaveCandles(c, Noise(c, 4, seed: 3), CancellationToken.None);

        Assert.That(await Store().ReadCandles(b, CancellationToken.None), Is.Null, "B is gone");
        Assert.That(await Store().ReadCandles(a, CancellationToken.None), Is.Not.Null);
        Assert.That(await Store().ReadCandles(c, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task A_month_removed_by_the_limit_is_saved_again_as_a_new_one()
    {
        (HistoryMonth a, HistoryMonth b, HistoryMonth c) = Months();
        FileHistoryMonthStore store = Store(await LimitOfTwoMonths());
        await Save(store, a, b);
        _time.Advance(TimeSpan.FromHours(2));
        await store.SaveCandles(c, Noise(c, 4, seed: 3), CancellationToken.None);
        Assert.That(await store.ReadCandles(a, CancellationToken.None), Is.Null);
        _time.Advance(TimeSpan.FromHours(2));

        await store.SaveCandles(a, Noise(a, 4, seed: 1), CancellationToken.None);

        Assert.That((await store.ReadCandles(a, CancellationToken.None))!.Items.SequenceEqual(Noise(a, 4, seed: 1).Items), Is.True, "the same answer as before");
        Assert.That(await store.ReadCandles(b, CancellationToken.None), Is.Null, "B is the oldest now");
        Assert.That(_folder.Months.Sum(path => new FileInfo(path).Length), Is.LessThanOrEqualTo(await LimitOfTwoMonths()));
    }

    // Task 3.4.

    [Test]
    public async Task A_month_saved_by_a_neighbour_is_read_without_the_service()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        await Caching(server, Store()).ReadCandles("btc", Minute, Day(2026, 3, 9), Day(2026, 3, 16), CancellationToken.None);
        server.Data.CandleRequests.Clear();

        HistoryRead<HistoryCandle> read = await Caching(server, Store()).ReadCandles("btc", Minute, Day(2026, 3, 16), Day(2026, 3, 23), CancellationToken.None);

        Assert.That(server.Data.CandleRequests, Is.Empty);
        Assert.That(read.Items, Has.Count.EqualTo(7 * 24 * 60));
    }

    [Test]
    public async Task Two_processes_that_write_one_month_at_once_leave_one_whole_file()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        HistoryRead<HistoryCandle> direct = await new GrpcHistorySource(server.Channel).ReadCandles("btc", FourHours, Day(2026, 3, 9), Day(2026, 3, 16), CancellationToken.None);

        for (int repeat = 0; repeat < 100; repeat++)
        {
            CachingHistorySource one = Caching(server, Store());
            CachingHistorySource other = Caching(server, Store());

            HistoryRead<HistoryCandle>[] reads = await Task.WhenAll(
                Task.Run(() => one.ReadCandles("btc", FourHours, Day(2026, 3, 9), Day(2026, 3, 16), CancellationToken.None)),
                Task.Run(() => other.ReadCandles("btc", FourHours, Day(2026, 3, 9), Day(2026, 3, 16), CancellationToken.None)));

            Assert.That(reads.Select(read => read.Items.SequenceEqual(direct.Items) && read.Coverage.SequenceEqual(direct.Coverage)), Has.All.True, $"repeat {repeat}");
            Assert.That(_folder.Months, Has.Length.EqualTo(1), $"repeat {repeat}");
            Assert.That(_folder.Temporaries, Is.Empty, $"repeat {repeat}");
            Assert.That(await Store().ReadCandles(new HistoryMonth("btc", FourHours, Day(2026, 3, 1)), CancellationToken.None), Is.Not.Null, $"repeat {repeat}");
            File.Delete(_folder.Months[0]);
        }
    }

    [Test]
    public async Task A_month_a_neighbour_just_removed_is_read_from_the_service()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        FileHistoryMonthStore store = Store();
        await Caching(server, store).ReadCandles("btc", FourHours, Day(2026, 3, 9), Day(2026, 3, 16), CancellationToken.None);
        CachingHistorySource reader = Caching(server, store);
        File.Delete(store.PathOf(new HistoryMonth("btc", FourHours, Day(2026, 3, 1))));
        server.Data.CandleRequests.Clear();

        HistoryRead<HistoryCandle> read = await reader.ReadCandles("btc", FourHours, Day(2026, 3, 9), Day(2026, 3, 16), CancellationToken.None);

        Assert.That(read.Items, Has.Count.EqualTo(7 * 6));
        Assert.That(server.Data.CandleRequests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task The_limit_counts_the_files_another_store_of_the_folder_wrote()
    {
        (HistoryMonth a, HistoryMonth b, HistoryMonth c) = Months();
        long limit = await LimitOfTwoMonths();
        await Save(Store(limit), a, b);
        _time.Advance(TimeSpan.FromHours(2));

        await Store(limit).SaveCandles(c, Noise(c, 4, seed: 3), CancellationToken.None);

        Assert.That(await Store().ReadCandles(a, CancellationToken.None), Is.Null, "A of the other store is gone");
        Assert.That(_folder.Months, Has.Length.EqualTo(2));
    }

    // Task 3.5.

    [Test]
    public async Task A_folder_that_is_a_file_gives_the_answer_of_the_service_no_month_and_one_warning()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_folder.Path)!);
        await File.WriteAllTextAsync(_folder.Path, "not a folder");
        var store = new FileHistoryMonthStore(_folder.Path, Unlimited, _time);
        var logger = new ListLogger();
        var caching = new CachingHistorySource(new GrpcHistorySource(server.Channel), store, _time, logger);

        try
        {
            HistoryRead<HistoryCandle> read = await caching.ReadCandles("btc", FourHours, Day(2026, 2, 1), Day(2026, 6, 1), CancellationToken.None);
            await caching.ReadFunding("btc", Day(2026, 2, 1), Day(2026, 6, 1), CancellationToken.None);

            HistoryRead<HistoryCandle> direct = await new GrpcHistorySource(server.Channel).ReadCandles("btc", FourHours, Day(2026, 2, 1), Day(2026, 6, 1), CancellationToken.None);
            Assert.That(read.Items, Is.EqualTo(direct.Items));
            Assert.That(await store.ReadCandles(new HistoryMonth("btc", FourHours, Day(2026, 3, 1)), CancellationToken.None), Is.Null);
            Assert.That(logger.Entries.Count(entry => entry.Level == LogLevel.Warning), Is.EqualTo(1));
        }
        finally
        {
            File.Delete(_folder.Path);
        }
    }

    // Task 3.6.

    /// <summary>
    ///     A month of minutes as the service gives them, 43 200 candles: the size of its file, the time of its write and of its
    ///     read, and what the read allocates, for «Замеры» of tasks.md; a file over 2 MB stops the change.
    /// </summary>
    [Test]
    public async Task A_month_of_minutes_takes_little_room_and_reads_quickly()
    {
        FileHistoryMonthStore store = Store();
        HistoryMonth warmup = March with { InstrumentId = "eth" };
        await store.SaveCandles(warmup, Minutes(warmup, 43_200, seed: 5), CancellationToken.None);
        await store.ReadCandles(warmup, CancellationToken.None);
        HistoryRead<HistoryCandle> month = Minutes(March, 43_200);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await store.SaveCandles(March, month, CancellationToken.None);
        TimeSpan write = clock.Elapsed;
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        clock.Restart();
        HistoryRead<HistoryCandle>? read = await store.ReadCandles(March, CancellationToken.None);
        TimeSpan reading = clock.Elapsed;
        allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        long size = new FileInfo(store.PathOf(March)).Length;

        await TestContext.Out.WriteLineAsync($"A month of 43 200 minutes: file {size / 1024.0:F0} KB, write {write.TotalMilliseconds:F0} ms, read {reading.TotalMilliseconds:F0} ms, read allocated {allocated / 1024.0 / 1024.0:F1} MB");
        Assert.That(read!.Items, Has.Count.EqualTo(43_200));
        Assert.That(size, Is.LessThanOrEqualTo(2 * Megabyte));
    }

    // Three months that take about 4 MB each.
    private static (HistoryMonth A, HistoryMonth B, HistoryMonth C) Months()
    {
        return (March, March with { Start = Day(2026, 4, 1) }, March with { Start = Day(2026, 5, 1) });
    }

    // Saves A, then B two hours later; both about 4 MB.
    private async Task Save(FileHistoryMonthStore store, HistoryMonth a, HistoryMonth b)
    {
        await store.SaveCandles(a, Noise(a, 4, seed: 1), CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(2));
        await store.SaveCandles(b, Noise(b, 4, seed: 2), CancellationToken.None);
        long[] sizes = [.. _folder.Months.Select(path => new FileInfo(path).Length)];
        Assert.That(sizes, Has.All.InRange(0.9 * sizes.Max(), sizes.Max()), "months of about the same size");
    }

    // Room for two months of Noise and not for three. GZip makes random text smaller by a ratio that depends on the zlib of
    // the platform (about 4 MB on macOS, 5.4 MB on the Linux of CI), so the size of one month is measured, not assumed.
    private async Task<long> LimitOfTwoMonths()
    {
        DirectoryInfo scratch = Directory.CreateTempSubdirectory("profit-fox-cache-size-");
        try
        {
            var store = new FileHistoryMonthStore(scratch.FullName, Unlimited, _time);
            await store.SaveCandles(March, Noise(March, 4, seed: 1), CancellationToken.None);
            return (long)(new FileInfo(store.PathOf(March)).Length * 2.5);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    // The kind a file of the cache writes after its mark and the number of its format.
    private static byte KindIn(string path)
    {
        byte[] bytes = Decompress(File.ReadAllBytes(path));
        return bytes[FileHistoryMonthStore.Mark.Length + sizeof(int)];
    }

    private FileHistoryMonthStore Store(long maxBytes = Unlimited)
    {
        return new FileHistoryMonthStore(_folder.Path, maxBytes, _time);
    }

    private CachingHistorySource Caching(MarketDataServer server, IHistoryMonthStore store)
    {
        return new CachingHistorySource(new GrpcHistorySource(server.Channel), store, _time, NullLogger.Instance);
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }

    private static byte[] Decompress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
        {
            gzip.CopyTo(output);
        }

        return output.ToArray();
    }
}
