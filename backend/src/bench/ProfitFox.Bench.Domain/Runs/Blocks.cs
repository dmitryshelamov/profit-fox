using ProfitFox.Bench.Domain.Hypotheses;

namespace ProfitFox.Bench.Domain.Runs;

/// <summary>
///     Blocks of time by number: UTC days, or weeks from Monday 00:00 UTC (1970-01-01 was a Thursday).
/// </summary>
public static class Blocks
{
    private static readonly long DayTicks = TimeSpan.TicksPerDay;

    public static long Of(DateTimeOffset time, TimeBlock block)
    {
        (long length, long offset) = Shape(block);
        return FloorDivide(time.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks + offset, length);
    }

    public static DateTimeOffset Start(long index, TimeBlock block)
    {
        (long length, long offset) = Shape(block);
        return new DateTimeOffset(DateTimeOffset.UnixEpoch.UtcTicks + index * length - offset, TimeSpan.Zero);
    }

    private static (long Length, long Offset) Shape(TimeBlock block)
    {
        return block == TimeBlock.Day ? (DayTicks, 0) : (7 * DayTicks, 3 * DayTicks);
    }

    private static long FloorDivide(long value, long divisor)
    {
        long quotient = value / divisor;
        return value % divisor != 0 && value < 0 ? quotient - 1 : quotient;
    }
}
