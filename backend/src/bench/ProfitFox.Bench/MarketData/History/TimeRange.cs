namespace ProfitFox.Bench.MarketData.History;

/// <summary>
///     A part of time from <see cref="From" />, inclusive, until <see cref="Until" />, exclusive.
/// </summary>
public readonly record struct TimeRange(DateTimeOffset From, DateTimeOffset Until)
{
    public bool Contains(DateTimeOffset from, DateTimeOffset until)
    {
        return From <= from && until <= Until;
    }
}
