using System.Globalization;

using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.Services.Runs;

/// <summary>
///     The minute path of a span from what history gave: prices of the minutes with candles, NaN for covered minutes
///     without one, unknown outside the coverage; funding payments of the pair with the covered part of funding around the
///     span.
/// </summary>
public static class MinutePaths
{
    public static MinutePath Build(
        DateTimeOffset from,
        DateTimeOffset to,
        HistoryRead<HistoryCandle> minutes,
        HistoryRead<HistoryPayment> funding)
    {
        int length = (int)((to - from).Ticks / TimeSpan.TicksPerMinute);
        var open = new double[length];
        var high = new double[length];
        var low = new double[length];
        var close = new double[length];
        var covered = new bool[length];
        Array.Fill(open, double.NaN);
        Array.Fill(high, double.NaN);
        Array.Fill(low, double.NaN);
        Array.Fill(close, double.NaN);
        foreach (TimeRange range in minutes.Coverage)
        {
            int first = Math.Max(0, Index(from, range.From));
            int last = Math.Min(length, Index(from, range.Until));
            for (int minute = first; minute < last; minute++)
            {
                covered[minute] = true;
            }
        }

        foreach (HistoryCandle candle in minutes.Items)
        {
            int minute = Index(from, candle.OpenTime);
            if (minute >= 0 && minute < length)
            {
                open[minute] = Parse(candle.Open);
                high[minute] = Parse(candle.High);
                low[minute] = Parse(candle.Low);
                close[minute] = Parse(candle.Close);
            }
        }

        // The known part of funding is the covered range the span starts in; without one every trade is left out on funding.
        TimeRange known = funding.Coverage.FirstOrDefault(range => range.From <= from && from < range.Until, new TimeRange(from, from));
        (DateTimeOffset, double)[] payments = [.. funding.Items.Select(payment => (payment.Time, Parse(payment.Rate)))];
        return new MinutePath(from, open, high, low, close, covered, payments, known.From, known.Until);
    }

    private static int Index(DateTimeOffset from, DateTimeOffset time)
    {
        return (int)((time - from).Ticks / TimeSpan.TicksPerMinute);
    }

    private static double Parse(string text)
    {
        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
