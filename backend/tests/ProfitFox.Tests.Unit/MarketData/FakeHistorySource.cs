using System.Globalization;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.MarketData;

namespace ProfitFox.Tests.Unit.MarketData;

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
                string price = (100 + (open - CoveredFrom).TotalHours / 4).ToString(CultureInfo.InvariantCulture);
                candles.Add(new HistoryCandle(open, price, price, price, price, Quiet.Contains(open) ? "0" : "12.5", Turnover: null, BuyVolume: "6"));
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
                payments.Add(new HistoryPayment(time, "0.0001"));
            }
        }

        return Task.FromResult(new HistoryRead<HistoryPayment>(coverage, payments));
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
