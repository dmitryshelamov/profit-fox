using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Services.Market;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Scanner;

/// <summary>
///     What the scanner holds in memory (decision 9): the pairs with their coverage, and of each the minutes of the last day, the points of
///     open interest of the last day and the last ticker. The stream, the loads and the minute cycle change it from their own threads, so
///     every member takes one lock; none of them waits on anything.
/// </summary>
public sealed class ScannerMarket
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new Lock();
    private readonly Dictionary<Instrument, Book> _books = [];
    private readonly HashSet<Instrument> _changed = [];

    /// <summary>Adds the pairs or replaces the records of the known ones; what memory holds of a known pair stays.</summary>
    public void Keep(IEnumerable<ScannerPair> pairs)
    {
        lock (_lock)
        {
            foreach (ScannerPair pair in pairs)
            {
                if (_books.TryGetValue(pair.Instrument, out Book? book))
                {
                    book.Pair = pair;
                }
                else
                {
                    _books[pair.Instrument] = new Book(pair);
                }

                _changed.Add(pair.Instrument);
            }
        }
    }

    /// <summary>
    ///     The pairs the list of the exchange changed, from a copy taken before its requests: a new pair is added, a known one takes what the
    ///     list tells (listed, the step of price, non-crypto) and keeps the coverage memory holds, which the stream may have moved meanwhile.
    /// </summary>
    public void KeepListed(IEnumerable<ScannerPair> pairs)
    {
        lock (_lock)
        {
            foreach (ScannerPair pair in pairs)
            {
                if (_books.TryGetValue(pair.Instrument, out Book? book))
                {
                    book.Pair = book.Pair with { Listed = pair.Listed, TickSize = pair.TickSize, TradFi = pair.TradFi };
                }
                else
                {
                    _books[pair.Instrument] = new Book(pair);
                }

                _changed.Add(pair.Instrument);
            }
        }
    }

    /// <summary>Every pair the scanner knows, listed or not.</summary>
    public IReadOnlyList<ScannerPair> All()
    {
        lock (_lock)
        {
            return [.. _books.Values.Select(book => book.Pair)];
        }
    }

    public IReadOnlyList<ScannerPair> Listed()
    {
        lock (_lock)
        {
            return [.. _books.Values.Select(book => book.Pair).Where(pair => pair.Listed)];
        }
    }

    public ScannerPair? Find(Instrument instrument)
    {
        lock (_lock)
        {
            return _books.GetValueOrDefault(instrument)?.Pair;
        }
    }

    /// <summary>Whether memory holds minutes of the pair that join its coverage; without them a restart reads the last day from storage.</summary>
    public bool HoldsMinutes(Instrument instrument)
    {
        lock (_lock)
        {
            return _books.GetValueOrDefault(instrument)?.MinutesFrom is not null;
        }
    }

    /// <summary>
    ///     Every minute of <c>[from, to)</c> is known, from the exchange or from storage: the minutes come into memory and the coverage moves
    ///     as <see cref="ScannerPair.WithLoaded" /> says.
    /// </summary>
    public void Loaded(Instrument instrument, DateTimeOffset from, DateTimeOffset to, IEnumerable<Candle> candles)
    {
        lock (_lock)
        {
            Book book = _books[instrument];

            foreach (Candle candle in candles)
            {
                book.Minutes[candle.OpenTime] = Minute(candle);
            }

            // Memory holds the minutes from MinutesFrom to the coverage without a hole.
            if (book.MinutesFrom is null ? book.Pair.CoveredUntil is null || to >= book.Pair.CoveredUntil : from < book.MinutesFrom && to >= book.MinutesFrom)
            {
                book.MinutesFrom = from;
            }

            Change(book, book.Pair.WithLoaded(from, to));
        }
    }

    /// <summary>A minute that closed on the stream.</summary>
    public void Closed(Instrument instrument, Candle candle)
    {
        lock (_lock)
        {
            if (_books.TryGetValue(instrument, out Book? book))
            {
                book.Minutes[candle.OpenTime] = Minute(candle);
                Change(book, book.Pair.WithClosedMinute(candle.OpenTime));
            }
        }
    }

    /// <summary>Every point of open interest of <c>[from, to)</c> is known.</summary>
    public void OpenInterestLoaded(Instrument instrument, DateTimeOffset from, DateTimeOffset to, IEnumerable<OpenInterestPoint> points)
    {
        lock (_lock)
        {
            Book book = _books[instrument];

            foreach (OpenInterestPoint point in points)
            {
                book.OpenInterest[point.Time] = point;
            }

            Change(book, book.Pair.WithOpenInterestLoaded(from, to));
        }
    }

    /// <summary>The point of open interest of a step, as the ticker gives it.</summary>
    public void OpenInterestPoint(Instrument instrument, OpenInterestPoint point)
    {
        lock (_lock)
        {
            Book book = _books[instrument];
            book.OpenInterest[point.Time] = point;
            Change(book, book.Pair.WithOpenInterestPoint(point.Time));
        }
    }

    public void Ticker(MarketTicker ticker)
    {
        lock (_lock)
        {
            if (_books.TryGetValue(ticker.Instrument, out Book? book))
            {
                book.Ticker = ticker;
            }
        }
    }

    public MarketTicker? TickerOf(Instrument instrument)
    {
        lock (_lock)
        {
            return _books.GetValueOrDefault(instrument)?.Ticker;
        }
    }

    /// <summary>Changes the record of a pair, such as once its history is loaded.</summary>
    public void Update(Instrument instrument, Func<ScannerPair, ScannerPair> change)
    {
        lock (_lock)
        {
            Book book = _books[instrument];
            Change(book, change(book.Pair));
        }
    }

    /// <summary>
    ///     The rows of every listed pair on the minute that ends at <paramref name="end" />; afterwards memory lets go of what the next
    ///     snapshots no longer need.
    /// </summary>
    public IReadOnlyList<MarketSnapshotRow> Snapshot(DateTimeOffset end, DateTimeOffset computedAt)
    {
        lock (_lock)
        {
            var rows = new List<MarketSnapshotRow>();

            foreach (Book book in _books.Values.Where(book => book.Pair.Listed))
            {
                PairMetrics metrics = MarketMetrics.Compute(
                    end,
                    book.MinutesFrom,
                    book.Pair.CoveredUntil,
                    [.. book.Minutes.Values],
                    [.. book.OpenInterest.Values],
                    book.Ticker);
                rows.Add(new MarketSnapshotRow(book.Pair.Instrument, computedAt, metrics, book.Pair.IsBehind(end)));
                Trim(book, end);
            }

            return rows;
        }
    }

    public IReadOnlyList<Instrument> Lagging(DateTimeOffset end)
    {
        lock (_lock)
        {
            return [.. _books.Values.Select(book => book.Pair).Where(pair => pair.Listed && pair.IsBehind(end)).Select(pair => pair.Instrument)];
        }
    }

    /// <summary>The pairs whose record changed since the last call, to be saved.</summary>
    public IReadOnlyList<ScannerPair> TakeChanged()
    {
        lock (_lock)
        {
            ScannerPair[] changed = [.. _changed.Select(instrument => _books[instrument].Pair)];
            _changed.Clear();

            return changed;
        }
    }

    public IReadOnlyList<MarketMinute> Minutes(Instrument instrument)
    {
        lock (_lock)
        {
            return [.. _books[instrument].Minutes.Values];
        }
    }

    public IReadOnlyList<OpenInterestPoint> OpenInterest(Instrument instrument)
    {
        lock (_lock)
        {
            return [.. _books[instrument].OpenInterest.Values];
        }
    }

    private void Change(Book book, ScannerPair pair)
    {
        if (pair != book.Pair)
        {
            book.Pair = pair;
            _changed.Add(pair.Instrument);
        }
    }

    // A minute without trades opens at the close before it, so the last minute before the day stays.
    private static void Trim(Book book, DateTimeOffset end)
    {
        DateTimeOffset dayStart = end - Day;
        DateTimeOffset[] old = [.. book.Minutes.Keys.TakeWhile(time => time < dayStart)];

        foreach (DateTimeOffset time in old.SkipLast(1))
        {
            book.Minutes.Remove(time);
        }

        foreach (DateTimeOffset time in book.OpenInterest.Keys.TakeWhile(time => time < dayStart - Step).ToList())
        {
            book.OpenInterest.Remove(time);
        }
    }

    private static MarketMinute Minute(Candle candle)
    {
        return new MarketMinute(candle.OpenTime, candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Turnover.Value);
    }

    private sealed class Book(ScannerPair pair)
    {
        public ScannerPair Pair { get; set; } = pair;

        /// <summary>The start of the minutes memory holds without a hole up to the coverage; <c>null</c> while it holds none of them.</summary>
        public DateTimeOffset? MinutesFrom { get; set; }

        public SortedDictionary<DateTimeOffset, MarketMinute> Minutes { get; } = [];

        public SortedDictionary<DateTimeOffset, OpenInterestPoint> OpenInterest { get; } = [];

        public MarketTicker? Ticker { get; set; }
    }
}