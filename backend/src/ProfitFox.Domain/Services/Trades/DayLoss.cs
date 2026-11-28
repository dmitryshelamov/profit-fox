namespace ProfitFox.Domain.Services.Trades;

/// <summary>
///     The loss of a day (change take-idea-trades, design.md, decision 11; spec trade-risk «Убыток за день»): the outcomes of
///     the trades closed since the start of the day UTC — or since the lift of the ban, when it was lifted later —, plus the
///     results of the open trades now. <see cref="Sum" /> is that sum, <see cref="Loss" /> the loss it makes, nought for a
///     day in the plus.
/// </summary>
public sealed record DayLoss(DateTimeOffset From, decimal Sum)
{
    public decimal Loss => Math.Max(0m, -Sum);

    /// <summary>
    ///     The start of the count: the start of the day UTC of <paramref name="now" />, or the lift of the ban when it is later.
    /// </summary>
    public static DateTimeOffset Start(DateTimeOffset now, DateTimeOffset? liftedAt)
    {
        var day = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        return liftedAt is { } lifted && lifted > day ? lifted : day;
    }

    /// <param name="closed">The trades closed with an outcome: when and with what.</param>
    /// <param name="open">The results now of the open trades.</param>
    /// <param name="now">The moment of the count.</param>
    /// <param name="liftedAt">When the ban was last lifted; null when never.</param>
    public static DayLoss Count(IEnumerable<(DateTimeOffset ClosedAt, decimal Result)> closed, IEnumerable<decimal> open, DateTimeOffset now, DateTimeOffset? liftedAt)
    {
        DateTimeOffset from = Start(now, liftedAt);
        decimal sum = closed.Where(trade => trade.ClosedAt >= from).Sum(trade => trade.Result) + open.Sum();
        return new DayLoss(from, sum);
    }
}
