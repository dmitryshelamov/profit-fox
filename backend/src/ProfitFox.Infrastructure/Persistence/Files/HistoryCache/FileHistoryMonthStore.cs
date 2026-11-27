using System.Globalization;
using System.IO.Compression;
using System.Text;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Persistence.Files.HistoryCache;

/// <summary>
///     Months of history in files of a folder (change add-backtest-history-cache, design.md, decisions 5–7):
///     <c>&lt;folder&gt;/&lt;pair&gt;/&lt;kind&gt;/&lt;yyyy-MM&gt;.gz</c>, one GZip stream a month with a header that names it. A
///     file comes under its name only written whole: it is written next to it under a temporary name and renamed. A file
///     that cannot be read, is cut short, is of another format or names another month is a month the store does not have.
///     The files take at most <c>maxBytes</c>: when more, the months read least recently go, down to 90 % of it. Several
///     processes may share the folder.
/// </summary>
internal sealed class FileHistoryMonthStore : IHistoryMonthStore
{
    /// <summary>
    ///     The number of the format of the files; a file of another number is not read.
    /// </summary>
    internal const int Format = 1;

    /// <summary>
    ///     The first bytes of every file, before the number of the format.
    /// </summary>
    internal static readonly byte[] Mark = "PFHC"u8.ToArray();

    private const string Extension = ".gz";
    private const string Temporary = ".tmp";
    private const byte HasTurnover = 1;
    private const byte HasBuyVolume = 2;

    /// <summary>
    ///     How old the read time of a file may be before a read moves it: the order of leaving is kept within an hour, without
    ///     a write to the disk on every read.
    /// </summary>
    private static readonly TimeSpan AccessStep = TimeSpan.FromHours(1);

    /// <summary>
    ///     How old a temporary file may be: older, its writer is gone.
    /// </summary>
    private static readonly TimeSpan TemporaryAge = TimeSpan.FromHours(1);

    /// <summary>
    ///     How much the store writes before it counts the folder again, to see the files of the other processes.
    /// </summary>
    private const long RecountAfter = 1L << 30;

    private readonly string _root;
    private readonly long _maxBytes;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _sizing = new SemaphoreSlim(1, 1);
    private bool _counted;
    private long _size;
    private long _written;

    /// <param name="root">The folder of the cache; made when the first month is saved.</param>
    /// <param name="maxBytes">How many bytes the files may take.</param>
    /// <param name="time">The clock of the read times of the files.</param>
    public FileHistoryMonthStore(string root, long maxBytes, TimeProvider time)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        _root = Path.GetFullPath(root);
        _maxBytes = maxBytes;
        _time = time;
    }

    /// <summary>
    ///     The file of a month in the folder.
    /// </summary>
    internal string PathOf(HistoryMonth month)
    {
        return Path.Combine(_root, SafeName(month.InstrumentId), KindName(month), $"{month.Start:yyyy-MM}{Extension}");
    }

    public async Task<HistoryRead<HistoryCandle>?> ReadCandles(HistoryMonth month, CancellationToken cancellation)
    {
        return await Read(month, reader => reader.ReadCandle(), cancellation);
    }

    public async Task<HistoryRead<HistoryPayment>?> ReadFunding(HistoryMonth month, CancellationToken cancellation)
    {
        return await Read(month, reader => new HistoryPayment(reader.ReadTime(), reader.ReadString()), cancellation);
    }

    // A point keeps its value as the text of its decimal, which gives the same decimal back (change add-idea-score).
    public async Task<HistoryRead<OpenInterestPoint>?> ReadOpenInterest(HistoryMonth month, CancellationToken cancellation)
    {
        return await Read(
            month,
            reader => new OpenInterestPoint(reader.ReadTime(), decimal.Parse(reader.ReadString(), NumberStyles.Float, CultureInfo.InvariantCulture)),
            cancellation);
    }

    public Task SaveCandles(HistoryMonth month, HistoryRead<HistoryCandle> read, CancellationToken cancellation)
    {
        return Save(month, read, (writer, candle) =>
        {
            writer.Write(candle.OpenTime.UtcTicks);
            writer.Write(candle.Open);
            writer.Write(candle.High);
            writer.Write(candle.Low);
            writer.Write(candle.Close);
            writer.Write(candle.Volume);
            writer.Write((byte)((candle.Turnover is null ? 0 : HasTurnover) | (candle.BuyVolume is null ? 0 : HasBuyVolume)));
            if (candle.Turnover is { } turnover)
            {
                writer.Write(turnover);
            }

            if (candle.BuyVolume is { } buyVolume)
            {
                writer.Write(buyVolume);
            }
        }, cancellation);
    }

    public Task SaveFunding(HistoryMonth month, HistoryRead<HistoryPayment> read, CancellationToken cancellation)
    {
        return Save(month, read, (writer, payment) =>
        {
            writer.Write(payment.Time.UtcTicks);
            writer.Write(payment.Rate);
        }, cancellation);
    }

    public Task SaveOpenInterest(HistoryMonth month, HistoryRead<OpenInterestPoint> read, CancellationToken cancellation)
    {
        return Save(month, read, (writer, point) =>
        {
            writer.Write(point.Time.UtcTicks);
            writer.Write(point.Value.ToString(CultureInfo.InvariantCulture));
        }, cancellation);
    }

    private async Task<HistoryRead<T>?> Read<T>(HistoryMonth month, Func<MonthReader, T> item, CancellationToken cancellation)
    {
        await Count(cancellation);
        string path = PathOf(month);
        try
        {
            HistoryRead<T>? read;
            await using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan))
            {
                // The whole file first: the reading of the items is then a loop over memory, with no await inside.
                using var compressed = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
                await file.CopyToAsync(compressed, cancellation);
                compressed.Position = 0;
                read = Parse(compressed, month, item);
            }

            if (read is not null)
            {
                Touch(path);
            }

            return read;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Not there, gone from under the read, cut short, broken or not a file of this store: a month the store has not.
            return null;
        }
    }

    private static HistoryRead<T>? Parse<T>(Stream compressed, HistoryMonth month, Func<MonthReader, T> item)
    {
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new MonthReader(gzip);
        if (!reader.ReadBytes(Mark.Length).AsSpan().SequenceEqual(Mark) || reader.ReadInt32() != Format)
        {
            return null;
        }

        byte kind = reader.ReadByte();
        long interval = reader.ReadInt64();
        string instrument = reader.ReadString();
        DateTimeOffset start = reader.ReadTime();
        if (kind != KindOf(month) || interval != (month.Interval?.Ticks ?? 0) || instrument != month.InstrumentId || start != month.Start)
        {
            return null;
        }

        int ranges = reader.ReadInt32();
        var coverage = new List<TimeRange>(Math.Clamp(ranges, 0, 64));
        for (int range = 0; range < ranges; range++)
        {
            coverage.Add(new TimeRange(reader.ReadTime(), reader.ReadTime()));
        }

        int count = reader.ReadInt32();
        var items = new List<T>(Math.Clamp(count, 0, 31 * 24 * 60));
        for (int index = 0; index < count; index++)
        {
            items.Add(item(reader));
        }

        // The mark again, then the end of the stream: a file cut after its last item is not taken either. The end of the
        // stream makes GZip check the CRC-32 and the length of what it gave.
        if (!reader.ReadBytes(Mark.Length).AsSpan().SequenceEqual(Mark) || gzip.ReadByte() != -1)
        {
            return null;
        }

        return new HistoryRead<T>(coverage, items);
    }

    private async Task Save<T>(HistoryMonth month, HistoryRead<T> read, Action<BinaryWriter, T> item, CancellationToken cancellation)
    {
        await Count(cancellation);
        string path = PathOf(month);
        string folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}{Temporary}");
        long length;
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous))
            {
                await using (var gzip = new GZipStream(file, CompressionLevel.Fastest, leaveOpen: true))
                {
                    using var buffer = new MemoryStream();
                    Write(buffer, month, read, item);
                    buffer.Position = 0;
                    await buffer.CopyToAsync(gzip, cancellation);
                }

                await file.FlushAsync(cancellation);
                length = file.Length;
            }

            cancellation.ThrowIfCancellationRequested();
            File.SetLastAccessTimeUtc(temporary, _time.GetUtcNow().UtcDateTime);
            long replaced = LengthOf(path);
            File.Move(temporary, path, overwrite: true);
            Interlocked.Add(ref _size, length - replaced);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        if (Interlocked.Add(ref _written, length) >= RecountAfter || Interlocked.Read(ref _size) > _maxBytes)
        {
            await Shrink(cancellation);
        }
    }

    private static void Write<T>(Stream stream, HistoryMonth month, HistoryRead<T> read, Action<BinaryWriter, T> item)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Mark);
        writer.Write(Format);
        writer.Write(KindOf(month));
        writer.Write(month.Interval?.Ticks ?? 0);
        writer.Write(month.InstrumentId);
        writer.Write(month.Start.UtcTicks);
        writer.Write(read.Coverage.Count);
        foreach (TimeRange range in read.Coverage)
        {
            writer.Write(range.From.UtcTicks);
            writer.Write(range.Until.UtcTicks);
        }

        writer.Write(read.Items.Count);
        foreach (T one in read.Items)
        {
            item(writer, one);
        }

        writer.Write(Mark);
    }

    // The size of the folder, counted on the first use of the store; the temporary files older than an hour go then.
    private async Task Count(CancellationToken cancellation)
    {
        if (Volatile.Read(ref _counted))
        {
            return;
        }

        await _sizing.WaitAsync(cancellation);
        try
        {
            if (!_counted)
            {
                Interlocked.Exchange(ref _size, Files().Sum(file => file.Length));
                Volatile.Write(ref _counted, true);
            }
        }
        finally
        {
            _sizing.Release();
        }
    }

    // Counts the folder again, sees the files of the other processes, and removes the months read least recently until the
    // files take at most 90 % of the limit.
    private async Task Shrink(CancellationToken cancellation)
    {
        await _sizing.WaitAsync(cancellation);
        try
        {
            Interlocked.Exchange(ref _written, 0);
            List<FileInfo> files = Files();
            long size = files.Sum(file => file.Length);
            if (size > _maxBytes)
            {
                long target = _maxBytes / 10 * 9;
                foreach (FileInfo file in files.OrderBy(file => file.LastAccessTimeUtc).ThenBy(file => file.FullName, StringComparer.Ordinal))
                {
                    if (size <= target)
                    {
                        break;
                    }

                    if (TryDelete(file.FullName))
                    {
                        size -= file.Length;
                    }
                }
            }

            Interlocked.Exchange(ref _size, size);
        }
        finally
        {
            _sizing.Release();
        }
    }

    // The files of months in the folder; the temporary files older than an hour are removed on the way.
    private List<FileInfo> Files()
    {
        List<FileInfo> files = [];
        if (!Directory.Exists(_root))
        {
            return files;
        }

        DateTime stale = (_time.GetUtcNow() - TemporaryAge).UtcDateTime;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (FileInfo file in new DirectoryInfo(_root).EnumerateFiles("*", options))
        {
            if (file.Name.EndsWith(Extension, StringComparison.Ordinal))
            {
                files.Add(file);
            }
            else if (file.Name.EndsWith(Temporary, StringComparison.Ordinal) && file.LastWriteTimeUtc < stale)
            {
                TryDelete(file.FullName);
            }
        }

        return files;
    }

    // Moves the read time of a file read now, when it is older than an hour; a file gone from under the read is left gone.
    private void Touch(string path)
    {
        try
        {
            DateTime now = _time.GetUtcNow().UtcDateTime;
            if (File.GetLastAccessTimeUtc(path) < now - AccessStep)
            {
                File.SetLastAccessTimeUtc(path, now);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The month was read; its place in the order of leaving is not worth a failure.
        }
    }

    // The length of a file, or nothing when it is not there: a neighbour may remove it at any moment.
    private static long LengthOf(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // The kind a file writes is the number of the kind of its month (change add-idea-score, design.md, decision 6): candles
    // and funding keep 1 and 2, so the files written before read as they did; open interest is 3.
    private static byte KindOf(HistoryMonth month)
    {
        return (byte)month.Kind;
    }

    /// <summary>
    ///     The name of the folder of the kind of a month: that of its candles or funding, or <c>open-interest</c>.
    /// </summary>
    internal static string KindName(HistoryMonth month)
    {
        return month.Kind == HistoryKind.OpenInterest ? "open-interest" : KindName(month.Interval);
    }

    /// <summary>
    ///     The name of the folder of a kind of history: <c>candles-1m</c> … <c>candles-1d</c>, or <c>funding</c>.
    /// </summary>
    internal static string KindName(TimeSpan? interval)
    {
        if (interval is not { } candles)
        {
            return "funding";
        }

        long minutes = (long)candles.TotalMinutes;
        string length = minutes % 1440 == 0 ? $"{minutes / 1440}d" : minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes}m";
        return $"candles-{length}";
    }

    /// <summary>
    ///     The id of an instrument as a name of a folder on any disk: lower-case letters, digits, <c>-</c> and <c>_</c> stay,
    ///     every other byte of its UTF-8 is <c>%XX</c>, so two ids never share a folder, also where names ignore the case.
    /// </summary>
    internal static string SafeName(string instrumentId)
    {
        var name = new StringBuilder(instrumentId.Length);
        foreach (byte one in Encoding.UTF8.GetBytes(instrumentId))
        {
            if (one is >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'_')
            {
                name.Append((char)one);
            }
            else
            {
                name.Append('%').Append(one.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return name.Length > 0 ? name.ToString() : "%";
    }

    /// <summary>
    ///     A reader of the body of a file: strings, times and the candles as they were written.
    /// </summary>
    private sealed class MonthReader(Stream stream) : BinaryReader(stream, Encoding.UTF8, leaveOpen: true)
    {
        public DateTimeOffset ReadTime()
        {
            return new DateTimeOffset(ReadInt64(), TimeSpan.Zero);
        }

        public HistoryCandle ReadCandle()
        {
            DateTimeOffset open = ReadTime();
            string o = ReadString();
            string high = ReadString();
            string low = ReadString();
            string close = ReadString();
            string volume = ReadString();
            byte present = ReadByte();
            string? turnover = (present & HasTurnover) != 0 ? ReadString() : null;
            string? buyVolume = (present & HasBuyVolume) != 0 ? ReadString() : null;
            return new HistoryCandle(open, o, high, low, close, volume, turnover, buyVolume);
        }
    }
}
