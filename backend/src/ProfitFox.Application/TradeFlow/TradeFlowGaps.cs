using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Application.TradeFlow;

/// <summary>
///     The gaps of the trade flow of a pair in a period (decision 9 of collect-trade-flow): the breaks of the stream cut by the days UTC, without
///     the parts in a day the archive rebuilt for the pair, and cut to the period.
/// </summary>
public static class TradeFlowGaps
{
    public static IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> Of(
        IReadOnlyCollection<StreamBreak> breaks,
        IReadOnlyCollection<DateTimeOffset> rebuiltDays,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var gaps = new List<(DateTimeOffset From, DateTimeOffset To)>();

        foreach (StreamBreak streamBreak in breaks)
        {
            DateTimeOffset start = streamBreak.LostAt > from ? streamBreak.LostAt : from;
            DateTimeOffset end = streamBreak.RestoredAt < to ? streamBreak.RestoredAt : to;

            while (start < end)
            {
                var day = new DateTimeOffset(start.UtcDateTime.Date, TimeSpan.Zero);
                DateTimeOffset dayEnd = day.AddDays(1) < end ? day.AddDays(1) : end;

                if (!rebuiltDays.Contains(day))
                {
                    gaps.Add((start, dayEnd));
                }

                start = dayEnd;
            }
        }

        return gaps;
    }
}