namespace ProfitFox.Domain.Entities.MarketData.Enums;

public static class IntervalExtensions
{
    /// <summary>End of the candle that opens at <paramref name="openTime"/>; a monthly candle ends at the next calendar month.</summary>
    public static DateTimeOffset CandleEnd(this Interval interval, DateTimeOffset openTime) => interval switch
    {
        Interval.OneMinute => openTime.AddMinutes(1),
        Interval.ThreeMinutes => openTime.AddMinutes(3),
        Interval.FiveMinutes => openTime.AddMinutes(5),
        Interval.FifteenMinutes => openTime.AddMinutes(15),
        Interval.ThirtyMinutes => openTime.AddMinutes(30),
        Interval.OneHour => openTime.AddHours(1),
        Interval.TwoHours => openTime.AddHours(2),
        Interval.FourHours => openTime.AddHours(4),
        Interval.SixHours => openTime.AddHours(6),
        Interval.TwelveHours => openTime.AddHours(12),
        Interval.OneDay => openTime.AddDays(1),
        Interval.OneWeek => openTime.AddDays(7),
        _ => openTime.AddMonths(1),
    };
}
