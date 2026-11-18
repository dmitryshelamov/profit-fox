using ProfitFox.Engine.Strategy.Execution;

namespace ProfitFox.Engine.Simulation.Market;

/// <summary>
///     The minutes of one pair: its minute candles, the spans the candles are known for, its funding payments and the span they are
///     known for. The bench builds it from the rows of the <c>History</c> stream. Minutes are kept on a grid from the start of the
///     first covered span, so the lookups need no pass over the candles.
/// </summary>
public sealed class MinutePath
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private readonly DateTimeOffset _start;
    private readonly MinuteCandle?[] _traded;
    private readonly int[] _uncoveredBefore;
    private readonly int[] _nextTraded;
    private readonly decimal?[] _lastTradedClose;
    private readonly DateTimeOffset[] _paymentTimes;

    /// <summary>
    ///     The instrument of the path.
    /// </summary>
    public string InstrumentId { get; }

    /// <summary>
    ///     The funding payments of the instrument in time order.
    /// </summary>
    public IReadOnlyList<FundingPayment> Payments { get; }

    /// <summary>
    ///     The span the funding payments are known for in full.
    /// </summary>
    public TimeRange FundingCoverage { get; }

    /// <exception cref="ArgumentException">
    ///     A span or candle is not on whole minutes, the spans overlap or are out of order, a candle lies outside them or repeats or is
    ///     out of order, or a payment is of another instrument or out of order.
    /// </exception>
    public MinutePath(
        string instrumentId,
        IReadOnlyList<MinuteCandle> candles,
        IReadOnlyList<TimeRange> candleCoverage,
        IReadOnlyList<FundingPayment> payments,
        TimeRange fundingCoverage)
    {
        CheckCoverage(candleCoverage);
        CheckPayments(instrumentId, payments);
        InstrumentId = instrumentId;
        Payments = payments;
        FundingCoverage = fundingCoverage;
        _paymentTimes = payments.Select(payment => payment.Time).ToArray();

        _start = candleCoverage.Count == 0 ? default : candleCoverage[0].Start;
        int minutes = candleCoverage.Count == 0 ? 0 : MinutesBetween(_start, candleCoverage[^1].End);
        var covered = new bool[minutes];
        foreach (TimeRange range in candleCoverage)
        {
            Array.Fill(covered, value: true, MinutesBetween(_start, range.Start), MinutesBetween(range.Start, range.End));
        }

        _traded = new MinuteCandle?[minutes];
        DateTimeOffset? previous = null;
        foreach (MinuteCandle candle in candles)
        {
            if (!IsWholeMinute(candle.OpenTime) || candle.OpenTime <= previous)
            {
                throw new ArgumentException($"The candle at {candle.OpenTime:O} is not on a whole minute after the one before it.", nameof(candles));
            }

            int index = IndexOf(candle.OpenTime);
            if (index < 0 || index >= minutes || !covered[index])
            {
                throw new ArgumentException($"The candle at {candle.OpenTime:O} lies outside the coverage of the candles.", nameof(candles));
            }

            previous = candle.OpenTime;
            _traded[index] = candle.Volume > 0 ? candle : null;
        }

        _uncoveredBefore = new int[minutes + 1];
        _lastTradedClose = new decimal?[minutes];
        for (int index = 0; index < minutes; index++)
        {
            _uncoveredBefore[index + 1] = _uncoveredBefore[index] + (covered[index] ? 0 : 1);
            _lastTradedClose[index] = _traded[index]?.Close ?? (index > 0 ? _lastTradedClose[index - 1] : null);
        }

        _nextTraded = new int[minutes + 1];
        _nextTraded[minutes] = -1;
        for (int index = minutes - 1; index >= 0; index--)
        {
            _nextTraded[index] = _traded[index] is null ? _nextTraded[index + 1] : index;
        }
    }

    /// <summary>
    ///     The first minute with trades that starts at or after <paramref name="moment" />, when every minute from the moment to it is
    ///     covered; null when a minute between is not covered or the path ends first.
    /// </summary>
    public MinuteCandle? FirstTradeAtOrAfter(DateTimeOffset moment)
    {
        int from = IndexOf(moment);
        if (from < 0 || from >= _traded.Length)
        {
            return null;
        }

        int next = _nextTraded[from];
        return next >= 0 && IsCoveredIndexes(from, next + 1) ? _traded[next] : null;
    }

    /// <summary>
    ///     Whether every minute of <c>[from, to)</c> lies on the path and is covered.
    /// </summary>
    public bool IsCovered(DateTimeOffset from, DateTimeOffset to)
    {
        int first = IndexOf(from);
        int end = IndexOf(to);
        return first >= 0 && end <= _traded.Length && IsCoveredIndexes(first, end);
    }

    /// <summary>
    ///     The close of the last minute with trades that ends at or before <paramref name="moment" />; null when there is none.
    /// </summary>
    public decimal? LastTradedCloseAt(DateTimeOffset moment)
    {
        int index = Math.Min(IndexOf(moment) - 1, _traded.Length - 1);
        return index < 0 ? null : _lastTradedClose[index];
    }

    /// <summary>
    ///     The payments with times in <c>(from, to]</c>.
    /// </summary>
    public IEnumerable<FundingPayment> PaymentsAfterUpTo(DateTimeOffset from, DateTimeOffset to)
    {
        int index = UpperBound(from);
        while (index < _paymentTimes.Length && _paymentTimes[index] <= to)
        {
            yield return Payments[index];
            index++;
        }
    }

    /// <summary>
    ///     Whether <paramref name="moment" /> is a whole minute.
    /// </summary>
    public static bool IsWholeMinute(DateTimeOffset moment)
    {
        return moment.UtcTicks % Minute.Ticks == 0;
    }

    private static void CheckCoverage(IReadOnlyList<TimeRange> coverage)
    {
        DateTimeOffset? end = null;
        foreach (TimeRange range in coverage)
        {
            if (!IsWholeMinute(range.Start) || !IsWholeMinute(range.End) || range.End <= range.Start || range.Start < end)
            {
                throw new ArgumentException(
                    $"The covered span {range.Start:O}–{range.End:O} is not of whole minutes after the span before it.",
                    nameof(coverage));
            }

            end = range.End;
        }
    }

    private static void CheckPayments(string instrumentId, IReadOnlyList<FundingPayment> payments)
    {
        DateTimeOffset? previous = null;
        foreach (FundingPayment payment in payments)
        {
            if (payment.InstrumentId != instrumentId || payment.Time < previous)
            {
                throw new ArgumentException($"The payment of {payment.InstrumentId} at {payment.Time:O} is not of {instrumentId} in time order.", nameof(payments));
            }

            previous = payment.Time;
        }
    }

    private static int MinutesBetween(DateTimeOffset from, DateTimeOffset to)
    {
        return (int)((to - from).Ticks / Minute.Ticks);
    }

    private bool IsCoveredIndexes(int first, int end)
    {
        return _uncoveredBefore[end] - _uncoveredBefore[first] == 0;
    }

    // The minute of a moment on the grid, rounded down; may lie before or after the path.
    private int IndexOf(DateTimeOffset moment)
    {
        long ticks = (moment - _start).Ticks;
        long minutes = ticks / Minute.Ticks;
        return (int)(ticks < 0 && ticks % Minute.Ticks != 0 ? minutes - 1 : minutes);
    }

    // The index of the first payment after the moment.
    private int UpperBound(DateTimeOffset moment)
    {
        int low = 0;
        int high = _paymentTimes.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (_paymentTimes[middle] <= moment)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}