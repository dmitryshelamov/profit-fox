using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Services.MarketData;

/// <summary>
///     The source of history with the cache of the process (change add-backtest-history-cache, design.md, decisions 1–4, 8
///     and 9; spec market-history-cache). The window is cut into calendar months of UTC. A month the store has is taken from
///     it; a closed month the store has not — every day of it before the current UTC day — is read from the service whole
///     and kept when the service covered all of it; any other part of the window is read from the service as it is.
///     Neighbouring parts that go to the service are one request. The answer is the items of the window in the order of
///     time and the coverage cut to the window: what the service gives for the window. A closed month the service did not
///     cover whole is remembered until the process ends and then read by the window only.
///     A failure of the store is a month it does not have, or a month not kept, with a warning at most once a minute; an
///     error of the service goes on as it is, and a cancelled read keeps nothing.
/// </summary>
public sealed class CachingHistorySource(IHistorySource source, IHistoryMonthStore store, TimeProvider time, ILogger logger) : IHistorySource, IHistoryCacheCounters
{
    private static readonly TimeSpan WarningPause = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<HistoryMonth, bool> _notWhole = new ConcurrentDictionary<HistoryMonth, bool>();
    private readonly Lock _warning = new Lock();
    private DateTimeOffset? _warned;
    private long _fromCache;
    private long _saved;
    private long _requests;

    public HistoryCacheCounts Counts => new HistoryCacheCounts(Interlocked.Read(ref _fromCache), Interlocked.Read(ref _saved), Interlocked.Read(ref _requests));

    public Task<HistoryRead<HistoryCandle>> ReadCandles(string instrumentId, TimeSpan interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        // A candle of an interval that does not divide a day may cross the border of a month: such candles are not kept.
        if (interval <= TimeSpan.Zero || TimeSpan.TicksPerDay % interval.Ticks != 0)
        {
            Interlocked.Increment(ref _requests);
            return source.ReadCandles(instrumentId, interval, from, to, cancellation);
        }

        return Read(
            instrumentId,
            interval,
            from,
            to,
            candle => candle.OpenTime,
            (start, end) => source.ReadCandles(instrumentId, interval, start, end, cancellation),
            month => store.ReadCandles(month, cancellation),
            (month, read) => store.SaveCandles(month, read, cancellation),
            cancellation);
    }

    public Task<HistoryRead<HistoryPayment>> ReadFunding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        return Read(
            instrumentId,
            null,
            from,
            to,
            payment => payment.Time,
            (start, end) => source.ReadFunding(instrumentId, start, end, cancellation),
            month => store.ReadFunding(month, cancellation),
            (month, read) => store.SaveFunding(month, read, cancellation),
            cancellation);
    }

    private async Task<HistoryRead<T>> Read<T>(
        string instrumentId,
        TimeSpan? interval,
        DateTimeOffset from,
        DateTimeOffset to,
        Func<T, DateTimeOffset> timeOf,
        Func<DateTimeOffset, DateTimeOffset, Task<HistoryRead<T>>> readService,
        Func<HistoryMonth, Task<HistoryRead<T>?>> readStore,
        Func<HistoryMonth, HistoryRead<T>, Task> save,
        CancellationToken cancellation)
    {
        if (to <= from)
        {
            Interlocked.Increment(ref _requests);
            return await readService(from, to);
        }

        DateTimeOffset today = HistoryPeriod.Today(time.GetUtcNow());
        List<Part<T>> parts = [];
        for (DateTimeOffset start = HistoryMonth.StartOf(from); start < to; start = start.AddMonths(1))
        {
            var month = new HistoryMonth(instrumentId, interval, start);
            DateTimeOffset low = from > month.Start ? from : month.Start;
            DateTimeOffset high = to < month.End ? to : month.End;
            bool closed = month.End <= today && !_notWhole.ContainsKey(month);
            if (closed && await Kept(month, readStore) is { } kept)
            {
                Interlocked.Increment(ref _fromCache);
                parts.Add(new Part<T>(low, high, kept, []));
                continue;
            }

            (DateTimeOffset askFrom, DateTimeOffset askTo) = closed ? (month.Start, month.End) : (low, high);
            if (parts.Count > 0 && parts[^1].Read is null && parts[^1].AskTo == askFrom)
            {
                Part<T> last = parts[^1];
                parts[^1] = last with { Until = high, AskTo = askTo, Whole = closed ? [.. last.Whole, month] : last.Whole };
            }
            else
            {
                parts.Add(new Part<T>(low, high, null, closed ? [month] : []) { AskFrom = askFrom, AskTo = askTo });
            }
        }

        List<TimeRange> coverage = [];
        List<T> items = [];
        foreach (Part<T> part in parts)
        {
            HistoryRead<T> read = part.Read ?? await Ask(part, timeOf, readService, save, cancellation);
            coverage.AddRange(read.Coverage);
            items.AddRange(read.Items.Where(item => timeOf(item) >= part.From && timeOf(item) < part.Until));
        }

        return new HistoryRead<T>(Coverage.Trim(coverage, from, to), items);
    }

    // Reads a part from the service and keeps each of its closed months the service covered whole.
    private async Task<HistoryRead<T>> Ask<T>(
        Part<T> part,
        Func<T, DateTimeOffset> timeOf,
        Func<DateTimeOffset, DateTimeOffset, Task<HistoryRead<T>>> readService,
        Func<HistoryMonth, HistoryRead<T>, Task> save,
        CancellationToken cancellation)
    {
        Interlocked.Increment(ref _requests);
        HistoryRead<T> read = await readService(part.AskFrom, part.AskTo);
        foreach (HistoryMonth month in part.Whole)
        {
            IReadOnlyList<TimeRange> covered = Coverage.Trim(read.Coverage, month.Start, month.End);
            if (covered.Count != 1 || covered[0] != new TimeRange(month.Start, month.End))
            {
                _notWhole.TryAdd(month, true);
                continue;
            }

            cancellation.ThrowIfCancellationRequested();
            var kept = new HistoryRead<T>(covered, [.. read.Items.Where(item => timeOf(item) >= month.Start && timeOf(item) < month.End)]);
            try
            {
                await save(month, kept);
                Interlocked.Increment(ref _saved);
            }
            catch (Exception exception) when (!cancellation.IsCancellationRequested)
            {
                Warn($"The cache of history did not keep {Name(month)}: {exception.Message}");
            }
        }

        return new HistoryRead<T>(Coverage.Trim(read.Coverage, part.From, part.Until), read.Items);
    }

    private async Task<HistoryRead<T>?> Kept<T>(HistoryMonth month, Func<HistoryMonth, Task<HistoryRead<T>?>> readStore)
    {
        try
        {
            return await readStore(month);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Warn($"The cache of history did not read {Name(month)}: {exception.Message}");
            return null;
        }
    }

    private void Warn(string line)
    {
        DateTimeOffset now = time.GetUtcNow();
        lock (_warning)
        {
            if (_warned is { } warned && now - warned < WarningPause)
            {
                return;
            }

            _warned = now;
        }

        logger.LogWarning("{Line}", line);
    }

    private static string Name(HistoryMonth month)
    {
        string kind = month.Interval is { } interval ? $"candles {interval.TotalMinutes:F0}m" : "funding";
        return $"{kind} of {month.InstrumentId} for {month.Start:yyyy-MM}";
    }

    /// <summary>
    ///     A part of the window [<see cref="From" />, <see cref="Until" />): a month of the store, or a request
    ///     [<see cref="AskFrom" />, <see cref="AskTo" />) to the service with the closed months it reads whole.
    /// </summary>
    private sealed record Part<T>(DateTimeOffset From, DateTimeOffset Until, HistoryRead<T>? Read, IReadOnlyList<HistoryMonth> Whole)
    {
        public DateTimeOffset AskFrom { get; init; }

        public DateTimeOffset AskTo { get; init; }
    }
}
