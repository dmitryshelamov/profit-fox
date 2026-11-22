using System.Globalization;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Application.Queries.MarketData;

/// <summary>
///     History in memory: 4h candles and funding every 8 hours, covered from <see cref="CoveredFrom" />; it counts its reads.
/// </summary>
internal sealed class FakeHistorySource : IHistorySource
{
    public static readonly DateTimeOffset CoveredFrom = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    public List<(string Feed, DateTimeOffset From, DateTimeOffset To)> Reads { get; } = [];

    /// <summary>
    ///     A hole in the coverage, if any.
    /// </summary>
    public TimeRange? Hole { get; init; }

    /// <summary>
    ///     The rate of the payment at a moment; 0.0001 when not given.
    /// </summary>
    public Func<DateTimeOffset, string>? Rate { get; init; }

    /// <summary>
    ///     The price of a candle opened at a moment: open, high, low and close; when not given every price is
    ///     100 + the hours since <see cref="CoveredFrom" /> / 4.
    /// </summary>
    public Func<DateTimeOffset, (double Open, double High, double Low, double Close)>? Price { get; init; }

    /// <summary>
    ///     Candles opened at these moments have no trades.
    /// </summary>
    public HashSet<DateTimeOffset> Quiet { get; } = [];

    public Task<HistoryRead<HistoryCandle>> ReadCandles(string instrumentId, TimeSpan interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        Reads.Add(("candles", from, to));
        IReadOnlyList<TimeRange> coverage = Covered(from, to);
        List<HistoryCandle> candles = [];
        for (DateTimeOffset open = from; open < to; open += interval)
        {
            if (Coverage.Covers(coverage, open, open + interval))
            {
                double flat = 100 + (open - CoveredFrom).TotalHours / 4;
                (double o, double h, double l, double c) = Price?.Invoke(open) ?? (flat, flat, flat, flat);
                candles.Add(new HistoryCandle(open, Text(o), Text(h), Text(l), Text(c), Quiet.Contains(open) ? "0" : "12.5", Turnover: null, BuyVolume: "6"));
            }
        }

        return Task.FromResult(new HistoryRead<HistoryCandle>(coverage, candles));
    }

    public Task<HistoryRead<HistoryPayment>> ReadFunding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        Reads.Add(("funding", from, to));
        IReadOnlyList<TimeRange> coverage = Covered(from, to);
        List<HistoryPayment> payments = [];
        for (DateTimeOffset time = from; time < to; time += TimeSpan.FromHours(8))
        {
            if (Coverage.Covers(coverage, time, time.AddTicks(1)))
            {
                payments.Add(new HistoryPayment(time, Rate?.Invoke(time) ?? "0.0001"));
            }
        }

        return Task.FromResult(new HistoryRead<HistoryPayment>(coverage, payments));
    }

    private static string Text(double price)
    {
        return price.ToString(CultureInfo.InvariantCulture);
    }

    private IReadOnlyList<TimeRange> Covered(DateTimeOffset from, DateTimeOffset to)
    {
        var all = new TimeRange(CoveredFrom > from ? CoveredFrom : from, to);
        if (all.Until <= all.From)
        {
            return [];
        }

        if (Hole is not { } hole)
        {
            return [all];
        }

        return Coverage.Trim([new TimeRange(all.From, hole.From), new TimeRange(hole.Until, all.Until)], from, to);
    }
}
