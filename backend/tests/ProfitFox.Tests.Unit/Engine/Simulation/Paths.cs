using ProfitFox.Engine.Simulation.Trades;

namespace ProfitFox.Tests.Unit.Engine.Simulation;

/// <summary>
///     Minute paths for the tests, written minute by minute from <see cref="Start" />.
/// </summary>
internal sealed class Paths
{
    public static readonly DateTimeOffset Start = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly List<(double Open, double High, double Low, double Close, bool Covered)> _minutes = [];
    private readonly List<(DateTimeOffset, double)> _funding = [];

    public static DateTimeOffset At(int minute)
    {
        return Start + TimeSpan.FromMinutes(minute);
    }

    /// <summary>
    ///     A minute with trades.
    /// </summary>
    public Paths Minute(double open, double high, double low, double close)
    {
        _minutes.Add((open, high, low, close, true));
        return this;
    }

    /// <summary>
    ///     <paramref name="count" /> minutes at one price.
    /// </summary>
    public Paths Flat(double price, int count)
    {
        for (int index = 0; index < count; index++)
        {
            Minute(price, price, price, price);
        }

        return this;
    }

    /// <summary>
    ///     A covered minute without trades.
    /// </summary>
    public Paths Quiet(int count = 1)
    {
        for (int index = 0; index < count; index++)
        {
            _minutes.Add((double.NaN, double.NaN, double.NaN, double.NaN, true));
        }

        return this;
    }

    /// <summary>
    ///     A minute the history does not know.
    /// </summary>
    public Paths Unknown()
    {
        _minutes.Add((double.NaN, double.NaN, double.NaN, double.NaN, false));
        return this;
    }

    public Paths Payment(int minute, double rate)
    {
        _funding.Add((At(minute), rate));
        return this;
    }

    public MinutePath Build(bool withFunding = true)
    {
        return new MinutePath(
            Start,
            [.. _minutes.Select(minute => minute.Open)],
            [.. _minutes.Select(minute => minute.High)],
            [.. _minutes.Select(minute => minute.Low)],
            [.. _minutes.Select(minute => minute.Close)],
            [.. _minutes.Select(minute => minute.Covered)],
            _funding,
            withFunding ? Start : null,
            withFunding ? Start + TimeSpan.FromDays(30) : null);
    }
}
