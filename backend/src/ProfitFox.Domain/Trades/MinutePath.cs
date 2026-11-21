namespace ProfitFox.Domain.Trades;

/// <summary>
///     The minute path of one pair on a regular grid from <see cref="Start" />. A price is NaN for a minute without trades;
///     <c>covered</c> is false for a minute the history does not know. Funding payments are known in full inside
///     [<see cref="FundingFrom" />, <see cref="FundingUntil" />); a pair without funding has <see cref="FundingFrom" /> null.
/// </summary>
public sealed class MinutePath
{
    private readonly double[] _open;
    private readonly double[] _high;
    private readonly double[] _low;
    private readonly double[] _close;
    private readonly bool[] _covered;
    private readonly long[] _fundingMinutes;
    private readonly double[] _fundingCumulative;
    private readonly DateTimeOffset[] _fundingTimes;

    public DateTimeOffset Start { get; }

    public int Minutes => _open.Length;

    public DateTimeOffset? FundingFrom { get; }

    public DateTimeOffset? FundingUntil { get; }

    public MinutePath(
        DateTimeOffset start,
        double[] open,
        double[] high,
        double[] low,
        double[] close,
        bool[] covered,
        IReadOnlyList<(DateTimeOffset Time, double Rate)> funding,
        DateTimeOffset? fundingFrom = null,
        DateTimeOffset? fundingUntil = null)
    {
        if (start.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            throw new ArgumentException("The path starts on a minute.", nameof(start));
        }

        int length = open.Length;
        if (high.Length != length || low.Length != length || close.Length != length || covered.Length != length)
        {
            throw new ArgumentException("Every minute array has the same length.");
        }

        if ((fundingFrom is null) != (fundingUntil is null))
        {
            throw new ArgumentException("The coverage of funding has both ends or none.");
        }

        Start = start;
        _open = open;
        _high = high;
        _low = low;
        _close = close;
        _covered = covered;
        FundingFrom = fundingFrom;
        FundingUntil = fundingUntil;

        // As the Python bench: a payment is priced at the last close up to the minute before it, and the cumulative sum
        // of rate × price gives the funding of any span by two lookups.
        (DateTimeOffset Time, double Rate)[] payments = [.. funding.OrderBy(payment => payment.Time)];
        double[] lastClose = ForwardFill(close);
        _fundingTimes = [.. payments.Select(payment => payment.Time)];
        _fundingMinutes = [.. payments.Select(payment => payment.Time.UtcTicks)];
        _fundingCumulative = new double[payments.Length + 1];
        for (int index = 0; index < payments.Length; index++)
        {
            long minute = FloorDivide(payments[index].Time.UtcTicks - start.UtcTicks, TimeSpan.TicksPerMinute) - 1;
            double price = minute >= 0 && minute < length ? lastClose[minute] : double.NaN;
            double paid = payments[index].Rate * price;
            _fundingCumulative[index + 1] = _fundingCumulative[index] + (double.IsNaN(paid) ? 0 : paid);
        }
    }

    internal double Open(int minute)
    {
        return _open[minute];
    }

    internal double High(int minute)
    {
        return _high[minute];
    }

    internal double Low(int minute)
    {
        return _low[minute];
    }

    internal double Close(int minute)
    {
        return _close[minute];
    }

    internal bool Covered(int minute)
    {
        return _covered[minute];
    }

    /// <summary>
    ///     The index of the minute that starts at <paramref name="time" />; it may be outside the path.
    /// </summary>
    internal long MinuteIndex(DateTimeOffset time)
    {
        return FloorDivide(time.UtcTicks - Start.UtcTicks, TimeSpan.TicksPerMinute);
    }

    /// <summary>
    ///     The sum of rate × price of the payments at times <c>after &lt; t ≤ until</c>.
    /// </summary>
    internal double FundingPaid(DateTimeOffset after, DateTimeOffset until)
    {
        return _fundingCumulative[CountUpTo(until)] - _fundingCumulative[CountUpTo(after)];
    }

    // The number of payments at or before the time, as numpy's searchsorted with side right.
    private int CountUpTo(DateTimeOffset time)
    {
        int index = Array.BinarySearch(_fundingMinutes, time.UtcTicks);
        if (index < 0)
        {
            return ~index;
        }

        while (index < _fundingMinutes.Length && _fundingMinutes[index] == time.UtcTicks)
        {
            index++;
        }

        return index;
    }

    private static double[] ForwardFill(double[] values)
    {
        var filled = new double[values.Length];
        int last = 0;
        for (int index = 0; index < values.Length; index++)
        {
            if (!double.IsNaN(values[index]))
            {
                last = index;
            }

            filled[index] = values[last];
        }

        return filled;
    }

    private static long FloorDivide(long value, long divisor)
    {
        long quotient = value / divisor;
        return value % divisor != 0 && (value < 0) != (divisor < 0) ? quotient - 1 : quotient;
    }
}
