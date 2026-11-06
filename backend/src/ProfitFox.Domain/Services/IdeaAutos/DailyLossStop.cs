using ProfitFox.Domain.ValueObjects.IdeaAutos;

namespace ProfitFox.Domain.Services.IdeaAutos;

/// <summary>
///     The daily stop: the sum of the results in USDT after the fees of the real trades that closed since 00:00 UTC of the day of <c>now</c>. The
///     stop is triggered by the trade that first brought the sum, by the time of the close, to minus the limit, and holds to 00:00 UTC of the next
///     day even if the sum rises again. Open losses are not counted: the caller gives the closed trades only.
/// </summary>
public static class DailyLossStop
{
    public static DailyLossState Of(IEnumerable<(DateTimeOffset ClosedAt, decimal Result)> closed, DateTimeOffset now, decimal? limit)
    {
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        decimal sum = 0m;
        DateTimeOffset? triggered = null;

        foreach ((DateTimeOffset closedAt, decimal result) in closed.Where(trade => trade.ClosedAt >= dayStart).OrderBy(trade => trade.ClosedAt))
        {
            sum += result;

            if (triggered is null && sum <= -limit)
            {
                triggered = closedAt;
            }
        }

        return new DailyLossState(sum, limit, triggered, triggered is null ? null : dayStart.AddDays(1));
    }
}