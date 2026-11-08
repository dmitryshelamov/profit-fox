using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.MarketData;

/// <summary>
///     The history of a chart, shared by the chart of a bot and of a pair of the market: <c>from</c> and <c>to</c> are checked, <c>from</c>
///     goes down to the start of its candle, <c>to</c> is cut to the start of the current candle, at most 1000 candles, read through.
/// </summary>
public static class ChartCandles
{
    // The most candles of one page of the exchange.
    private const int MaxCandles = 1000;

    /// <param name="readThrough">Reads stored candles and fills the gaps from the exchange.</param>
    /// <param name="time">The clock that tells the current candle.</param>
    /// <param name="instrument">The instrument of the chart.</param>
    /// <param name="interval">The interval of the chart.</param>
    /// <param name="fromText">The start of the period as the user sent it.</param>
    /// <param name="toText">The end of the period as the user sent it.</param>
    /// <param name="errors">The errors of the caller's own parameters, to which the errors of the period are added.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The candles and whether the exchange could not fill the period; no candles when some parameter is invalid.</returns>
    public static async Task<(IReadOnlyList<Candle>? Candles, bool Unavailable)> ReadAsync(
        CandleReadThrough readThrough,
        TimeProvider time,
        Instrument instrument,
        Interval interval,
        string fromText,
        string toText,
        Dictionary<string, string> errors,
        CancellationToken cancellationToken)
    {
        if (!ApiTimestamps.TryParse(fromText, out DateTimeOffset from))
        {
            errors["from"] = ApiTimestamps.Invalid("from");
        }

        bool toIsTime = ApiTimestamps.TryParse(toText, out DateTimeOffset to);

        if (!toIsTime)
        {
            errors["to"] = ApiTimestamps.Invalid("to");
        }

        // An unparsed `from` stays at its minimum and never fails the period check.
        if (toIsTime && from >= to)
        {
            errors["from"] = "from must be earlier than to.";
        }

        if (errors.Count > 0)
        {
            return (null, false);
        }

        // A chart shows closed candles only: the current candle starts where the period ends at the latest.
        DateTimeOffset start = interval.CandleOpen(from);
        DateTimeOffset current = interval.CandleOpen(time.GetUtcNow() - CandleReadThrough.CloseMargin);
        DateTimeOffset end = to.ToUniversalTime() < current ? to.ToUniversalTime() : current;

        if (start >= end)
        {
            return ([], false);
        }

        if (interval.CandleCount(start, end, MaxCandles) > MaxCandles)
        {
            errors["from"] = $"The period must not be longer than {MaxCandles} candles.";

            return (null, false);
        }

        CandleReadThroughResult read = await readThrough.ReadAsync(instrument, interval, start, end, cancellationToken);

        return (read.Candles, read.Unavailable);
    }
}