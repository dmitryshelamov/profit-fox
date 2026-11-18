using System.Globalization;

using ProfitFox.Engine.Simulation.Market;
using ProfitFox.Engine.Strategy.Execution;

namespace ProfitFox.Tests.Unit.Engine.Simulation;

/// <summary>
///     Minute paths for the tests, written minute by minute from 10:00 of 2026-01-01.
/// </summary>
internal static class Paths
{
    public const string Instrument = "BTCUSDT";

    /// <summary>
    ///     10:00 of 2026-01-01, UTC.
    /// </summary>
    public static readonly DateTimeOffset Ten = At("2026-01-01T10:00");

    /// <summary>
    ///     A UTC moment written as <c>2026-01-01T10:00</c>.
    /// </summary>
    public static DateTimeOffset At(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    /// <summary>
    ///     The moment <paramref name="minutes" /> after 10:00.
    /// </summary>
    public static DateTimeOffset Minute(int minutes)
    {
        return Ten.AddMinutes(minutes);
    }

    /// <summary>
    ///     A candle of the minute <paramref name="minutes" /> after 10:00; volume 1 unless given.
    /// </summary>
    public static MinuteCandle Candle(int minutes, decimal open, decimal high, decimal low, decimal close, decimal volume = 1m)
    {
        return new MinuteCandle(Minute(minutes), open, high, low, close, volume);
    }

    /// <summary>
    ///     A candle of the minute whose prices are all <paramref name="price" />.
    /// </summary>
    public static MinuteCandle Flat(int minutes, decimal price, decimal volume = 1m)
    {
        return Candle(minutes, price, price, price, price, volume);
    }

    /// <summary>
    ///     A payment of the instrument <paramref name="minutes" /> after 10:00.
    /// </summary>
    public static FundingPayment Payment(int minutes, decimal rate)
    {
        return new FundingPayment(Instrument, Minute(minutes), rate);
    }

    /// <summary>
    ///     A path with the candles, covered from 10:00 for <paramref name="minutes" /> minutes unless spans are given, and payments
    ///     known for the same span unless one is given.
    /// </summary>
    public static MinutePath Path(
        int minutes,
        IEnumerable<MinuteCandle> candles,
        IEnumerable<FundingPayment>? payments = null,
        IReadOnlyList<TimeRange>? coverage = null,
        TimeRange? fundingCoverage = null)
    {
        var whole = new TimeRange(Ten, Minute(minutes));
        return new MinutePath(Instrument, candles.ToList(), coverage ?? [whole], (payments ?? []).ToList(), fundingCoverage ?? whole);
    }
}