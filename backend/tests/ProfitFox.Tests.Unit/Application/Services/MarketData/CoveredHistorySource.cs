using System.Globalization;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Application.Services.MarketData;

/// <summary>
///     The market-data service as the cache of history relies on it (change add-backtest-history-cache, design.md, «Risks»):
///     the coverage of a window is <see cref="Covered" /> cut to it, a candle belongs to a window by its open and is told when
///     its whole interval is covered, a payment every 8 hours is told when its moment is covered, and the items come in the
///     order of time. Opens and payments sit on the grid of their interval from the Unix epoch. Every field differs from
///     candle to candle, some turnovers and buy volumes are missing. It records its reads.
/// </summary>
internal sealed class CoveredHistorySource : IHistorySource
{
    public static readonly TimeSpan PaymentStep = TimeSpan.FromHours(8);

    /// <summary>
    ///     What the service holds; the whole time when not set.
    /// </summary>
    public List<TimeRange> Covered { get; init; } = [new TimeRange(DateTimeOffset.UnixEpoch, DateTimeOffset.MaxValue.AddYears(-1))];

    public List<(string Kind, DateTimeOffset From, DateTimeOffset To)> Reads { get; } = [];

    /// <summary>
    ///     Thrown by the next reads, one a read, before anything is answered.
    /// </summary>
    public Queue<Exception> Errors { get; } = new Queue<Exception>();

    /// <summary>
    ///     Called after a read is answered and before it is given back: a test cancels its read in it.
    /// </summary>
    public Action? AfterRead { get; set; }

    /// <summary>
    ///     History from <paramref name="listed" /> on, with <paramref name="holes" /> left out.
    /// </summary>
    public static CoveredHistorySource From(DateTimeOffset listed, params TimeRange[] holes)
    {
        List<TimeRange> covered = [];
        DateTimeOffset start = listed;
        foreach (TimeRange hole in holes.OrderBy(hole => hole.From))
        {
            covered.Add(new TimeRange(start, hole.From));
            start = hole.Until;
        }

        covered.Add(new TimeRange(start, DateTimeOffset.MaxValue.AddYears(-1)));
        return new CoveredHistorySource { Covered = covered };
    }

    public static string Kind(TimeSpan interval)
    {
        return $"candles {(long)interval.TotalMinutes}";
    }

    public Task<HistoryRead<HistoryCandle>> ReadCandles(string instrumentId, TimeSpan interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        Record(Kind(interval), from, to);
        List<HistoryCandle> candles = [];
        for (DateTimeOffset open = Grid(from, interval); open < to; open += interval)
        {
            if (Coverage.Covers(Covered, open, open + interval))
            {
                candles.Add(Candle(instrumentId, open));
            }
        }

        return Answer(new HistoryRead<HistoryCandle>(Coverage.Trim(Covered, from, to), candles));
    }

    public Task<HistoryRead<HistoryPayment>> ReadFunding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        Record("funding", from, to);
        List<HistoryPayment> payments = [];
        for (DateTimeOffset time = Grid(from, PaymentStep); time < to; time += PaymentStep)
        {
            if (Coverage.Covers(Covered, time, time.AddTicks(1)))
            {
                long index = (time - DateTimeOffset.UnixEpoch).Ticks / PaymentStep.Ticks;
                payments.Add(new HistoryPayment(time, (index % 5 == 0 ? -0.0001m * (index % 7) : 0.0001m * (index % 11)).ToString(CultureInfo.InvariantCulture)));
            }
        }

        return Answer(new HistoryRead<HistoryPayment>(Coverage.Trim(Covered, from, to), payments));
    }

    private void Record(string kind, DateTimeOffset from, DateTimeOffset to)
    {
        lock (Reads)
        {
            Reads.Add((kind, from, to));
        }
    }

    private Task<HistoryRead<T>> Answer<T>(HistoryRead<T> read)
    {
        if (Errors.TryDequeue(out Exception? error))
        {
            return Task.FromException<HistoryRead<T>>(error);
        }

        AfterRead?.Invoke();
        return Task.FromResult(read);
    }

    // The first moment of the grid of step at or after from.
    private static DateTimeOffset Grid(DateTimeOffset from, TimeSpan step)
    {
        long ticks = (from - DateTimeOffset.UnixEpoch).Ticks;
        long steps = (ticks + step.Ticks - 1) / step.Ticks;
        return DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(steps * step.Ticks);
    }

    private static HistoryCandle Candle(string instrumentId, DateTimeOffset open)
    {
        long minute = (long)(open - DateTimeOffset.UnixEpoch).TotalMinutes + instrumentId.Length;
        decimal price = 100 + (minute % 997) / 8m;
        return new HistoryCandle(
            open,
            Text(price),
            Text(price + 0.125m),
            Text(price - 0.5m),
            Text(price + (minute % 3) / 4m),
            minute % 13 == 0 ? "0" : Text((minute % 101) * 1.5m),
            minute % 5 == 0 ? null : Text((minute % 101) * 150.25m),
            minute % 7 == 0 ? null : Text(minute % 43));
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
