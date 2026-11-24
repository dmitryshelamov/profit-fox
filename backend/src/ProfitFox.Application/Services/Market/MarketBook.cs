using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Application.Services.Market;

/// <summary>
///     The market in the memory of the scanner (change add-ideas-and-market, design.md, decision 3): for each pair its closed
///     minutes of the last day and an hour, the last point of open interest, the last funding rate and the payments of 48
///     hours. The candles of a timeframe, the windows of «Рынок» and the minutes of the outcome of an idea come from here.
///     A minute that comes again replaces the one the book has by pair and open time. Not thread-safe: one reader applies
///     the feed and asks.
/// </summary>
public sealed class MarketBook(TimeProvider time, TimeSpan closeDelay)
{
    /// <summary>
    ///     How long minutes are kept before the newest one of the pair: a day for the windows, an hour more for the minute
    ///     before the window of 24 hours.
    /// </summary>
    public static readonly TimeSpan Kept = TimeSpan.FromHours(25);

    // How late after the moment a window ago the book of a pair may begin to count the window from its first minute.
    private static readonly TimeSpan BookStartGrace = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan PaymentsKept = TimeSpan.FromHours(48);
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private readonly Dictionary<string, Pair> _pairs = new Dictionary<string, Pair>(StringComparer.Ordinal);

    /// <summary>
    ///     The open time of the newest closed minute the feed told of any pair; null before the first.
    /// </summary>
    public DateTimeOffset? LastMinute { get; private set; }

    /// <summary>
    ///     Since when the book holds every minute the feed told without a hole: the open time of its first minute, or the end
    ///     of the last hole the feed told; null before the first minute. A candle opened earlier may lack minutes (task 19.3).
    /// </summary>
    public DateTimeOffset? CoveredFrom { get; private set; }

    public IReadOnlyList<string> Symbols => [.. _pairs.Keys.Order(StringComparer.Ordinal)];

    /// <summary>
    ///     The open time of the last closed minute of the pair the feed told; null before its first.
    /// </summary>
    public DateTimeOffset? LastMinuteOf(string symbol)
    {
        return _pairs.TryGetValue(symbol, out Pair? pair) ? pair.Last?.OpenTime : null;
    }

    public void Apply(MarketEvent item)
    {
        switch (item)
        {
            case LiveMinute { Closed: true } minute:
                PairOf(minute.Symbol).Put(minute);
                LastMinute = LastMinute is { } last && last >= minute.OpenTime ? last : minute.OpenTime;
                CoveredFrom = CoveredFrom is { } covered && covered <= minute.OpenTime ? covered : minute.OpenTime;
                break;
            case FeedGap gap:
                CoveredFrom = CoveredFrom is { } since && since >= gap.Until ? since : gap.Until;
                break;
            case LivePoint { Kind: LivePointKind.OpenInterest } point:
                PairOf(point.Symbol).OpenInterest = Newer(PairOf(point.Symbol).OpenInterest, point);
                break;
            case LivePoint { Kind: LivePointKind.FundingRate } point:
                PairOf(point.Symbol).Rate = Newer(PairOf(point.Symbol).Rate, point);
                break;
            case LivePoint { Kind: LivePointKind.FundingPayment } point:
                PairOf(point.Symbol).Pay(new FundingPayment(point.Time, point.Value));
                break;
            case MarketPairs listed:
                var keep = new HashSet<string>(listed.Pairs.Select(pair => pair.Symbol), StringComparer.Ordinal);
                foreach (string symbol in _pairs.Keys.Where(symbol => !keep.Contains(symbol)).ToList())
                {
                    _pairs.Remove(symbol);
                }

                break;
        }
    }

    /// <summary>
    ///     Whether the candles with the close <paramref name="close" /> are closed for every pair (spec market-live-feed
    ///     «Момент закрытия свечи»): the clock passed the close and the delay, and the feed told a closed minute that ends at
    ///     the close or later. A minute without trades does not come, so the last minute of each pair is not awaited.
    /// </summary>
    public bool Closed(DateTimeOffset close)
    {
        return time.GetUtcNow() >= close + closeDelay && LastMinute is { } last && last + OneMinute >= close;
    }

    /// <summary>
    ///     Whether the feed told a closed minute that opens at <paramref name="moment" /> or later (task 25.1): a feed that
    ///     tells its minutes in the order of their open times, as a catch-up does, has then told the minute before the moment
    ///     of every pair.
    /// </summary>
    public bool Past(DateTimeOffset moment)
    {
        return LastMinute is { } last && last >= moment;
    }

    /// <summary>
    ///     Whether the book has the whole candle of <paramref name="timeframe" /> opened at <paramref name="open" />: the candle
    ///     is <see cref="Closed" /> and the book holds its minutes from its open on (task 19.3).
    /// </summary>
    public bool Holds(TimeSpan timeframe, DateTimeOffset open)
    {
        return CoveredFrom is { } covered && covered <= open && Closed(open + timeframe);
    }

    /// <summary>
    ///     The candle of <paramref name="timeframe" /> opened at <paramref name="open" />, built from the closed minutes of its
    ///     window as the service builds it; null when the pair has none there.
    /// </summary>
    public ClosedCandle? Candle(string symbol, TimeSpan timeframe, DateTimeOffset open)
    {
        if (!_pairs.TryGetValue(symbol, out Pair? pair))
        {
            return null;
        }

        LiveMinute? first = null;
        decimal high = 0;
        decimal low = 0;
        decimal close = 0;
        decimal volume = 0;
        foreach (LiveMinute minute in pair.Between(open, open + timeframe))
        {
            first ??= minute;
            high = minute == first ? minute.High : Math.Max(high, minute.High);
            low = minute == first ? minute.Low : Math.Min(low, minute.Low);
            close = minute.Close;
            volume += minute.Volume;
        }

        return first is null ? null : new ClosedCandle(open, open + timeframe, first.Open, high, low, close, volume);
    }

    /// <summary>
    ///     The closed minutes of a pair opened in [<paramref name="from" />, <paramref name="to" />), oldest first.
    /// </summary>
    public IEnumerable<LiveMinute> Minutes(string symbol, DateTimeOffset from, DateTimeOffset to)
    {
        return _pairs.TryGetValue(symbol, out Pair? pair) ? pair.Between(from, to) : [];
    }

    /// <summary>
    ///     The last payment of a pair before <paramref name="moment" />, as a graph reads it at a close: a payment at the close
    ///     belongs to the next candle, as in a backtest (task 27.3, <c>PairCandles.Closed</c>).
    /// </summary>
    public FundingPayment? LastPayment(string symbol, DateTimeOffset moment)
    {
        return _pairs.TryGetValue(symbol, out Pair? pair) ? pair.Payments.LastOrDefault(payment => payment.Time < moment) : null;
    }

    /// <summary>
    ///     The close of the last minute with trades of a pair opened before <paramref name="time" />: the price a payment at
    ///     that time is counted at, as the minute path of a backtest prices it; null when the book has none.
    /// </summary>
    public decimal? CloseBefore(string symbol, DateTimeOffset time)
    {
        return _pairs.TryGetValue(symbol, out Pair? pair) ? pair.AtOrBefore(time - OneMinute)?.Close : null;
    }

    /// <summary>
    ///     The payments of a pair the book keeps, oldest first.
    /// </summary>
    public IReadOnlyList<FundingPayment> Payments(string symbol)
    {
        return _pairs.TryGetValue(symbol, out Pair? pair) ? pair.Payments : [];
    }

    /// <summary>
    ///     The snapshot of a pair now (spec market-overview «Снимок рынка»); null for a pair without a minute.
    /// </summary>
    public MarketPair? Snapshot(string symbol)
    {
        if (!_pairs.TryGetValue(symbol, out Pair? pair) || pair.Last is not { } last)
        {
            return null;
        }

        // The minute at or before the moment the window ago, or — when the book of the pair begins no later than five minutes
        // after it, as right after the start (task 18.18) — its first minute.
        decimal? Change(TimeSpan window)
        {
            DateTimeOffset moment = last.OpenTime - window;
            LiveMinute? before = pair.AtOrBefore(moment) ?? pair.FirstAfter(moment, BookStartGrace);
            return before is { Close: > 0 } ? last.Close / before.Close - 1 : null;
        }

        decimal? day = Change(TimeSpan.FromHours(24));
        decimal? turnover = day is null ? null : pair.Between(last.OpenTime - TimeSpan.FromHours(24) + OneMinute, last.OpenTime + OneMinute).Sum(minute => minute.Turnover);
        IReadOnlyList<FundingPayment> payments = pair.Payments;
        DateTimeOffset? next = payments.Count >= 2 ? payments[^1].Time + (payments[^1].Time - payments[^2].Time) : null;
        return new MarketPair(
            symbol,
            last.Close,
            Change(TimeSpan.FromHours(1)),
            Change(TimeSpan.FromHours(4)),
            day,
            turnover,
            pair.OpenInterest?.Value,
            pair.OpenInterest is { } interest ? interest.Value * last.Close : null,
            pair.Rate?.Value,
            next,
            time.GetUtcNow());
    }

    private Pair PairOf(string symbol)
    {
        if (!_pairs.TryGetValue(symbol, out Pair? pair))
        {
            pair = new Pair();
            _pairs.Add(symbol, pair);
        }

        return pair;
    }

    private static LivePoint Newer(LivePoint? held, LivePoint point)
    {
        return held is not null && held.Time > point.Time ? held : point;
    }

    // The minutes of one pair in a ring by the minute of their open time: a minute that comes again takes its own slot,
    // and an old one is overwritten by one a ring later.
    private sealed class Pair
    {
        private static readonly int Size = (int)(Kept.Ticks / TimeSpan.TicksPerMinute);
        private readonly LiveMinute?[] _ring = new LiveMinute?[Size];
        private readonly List<FundingPayment> _payments = [];

        public LiveMinute? Last { get; private set; }

        public LivePoint? OpenInterest { get; set; }

        public LivePoint? Rate { get; set; }

        public IReadOnlyList<FundingPayment> Payments => _payments;

        public void Put(LiveMinute minute)
        {
            if (Last is { } last && minute.OpenTime <= last.OpenTime - Kept)
            {
                return;
            }

            _ring[Slot(minute.OpenTime)] = minute;
            if (Last is null || minute.OpenTime >= Last.OpenTime)
            {
                Last = minute;
            }
        }

        public void Pay(FundingPayment payment)
        {
            int index = _payments.FindIndex(held => held.Time >= payment.Time);
            if (index >= 0 && _payments[index].Time == payment.Time)
            {
                _payments[index] = payment;
            }
            else
            {
                _payments.Insert(index < 0 ? _payments.Count : index, payment);
            }

            DateTimeOffset newest = _payments[^1].Time;
            _payments.RemoveAll(held => held.Time < newest - PaymentsKept);
        }

        public IEnumerable<LiveMinute> Between(DateTimeOffset from, DateTimeOffset to)
        {
            if (Last is not { } last)
            {
                yield break;
            }

            DateTimeOffset start = from > last.OpenTime - Kept ? from : last.OpenTime - Kept + OneMinute;
            for (DateTimeOffset open = Floor(start); open < to && open <= last.OpenTime; open += OneMinute)
            {
                if (open >= from && _ring[Slot(open)] is { } minute && minute.OpenTime == open)
                {
                    yield return minute;
                }
            }
        }

        // The first minute opened after the moment and no later than the grace after it, among those kept.
        public LiveMinute? FirstAfter(DateTimeOffset moment, TimeSpan grace)
        {
            for (DateTimeOffset open = Floor(moment) + OneMinute; open <= moment + grace; open += OneMinute)
            {
                if (_ring[Slot(open)] is { } minute && minute.OpenTime == open)
                {
                    return minute;
                }
            }

            return null;
        }

        // The last minute opened at or before the moment, among those kept.
        public LiveMinute? AtOrBefore(DateTimeOffset moment)
        {
            if (Last is not { } last)
            {
                return null;
            }

            for (DateTimeOffset open = Floor(moment); open > last.OpenTime - Kept; open -= OneMinute)
            {
                if (_ring[Slot(open)] is { } minute && minute.OpenTime == open)
                {
                    return minute;
                }
            }

            return null;
        }

        private static int Slot(DateTimeOffset open)
        {
            long minute = open.UtcTicks / TimeSpan.TicksPerMinute;
            return (int)(((minute % Size) + Size) % Size);
        }

        private static DateTimeOffset Floor(DateTimeOffset moment)
        {
            return new DateTimeOffset(moment.UtcTicks - (moment.UtcTicks % TimeSpan.TicksPerMinute), TimeSpan.Zero);
        }
    }
}
