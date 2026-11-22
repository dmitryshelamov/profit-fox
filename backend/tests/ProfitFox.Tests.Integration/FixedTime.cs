namespace ProfitFox.Tests.Integration;

/// <summary>
///     A clock that always says <paramref name="now" />: what a preview of the test takes for the current day.
/// </summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    /// <summary>
    ///     A moment of 2026-05-16: the end of exploration of the previews is then 2026-05-16 00:00 UTC, the end of exploration
    ///     before change add-strategy-backtest.
    /// </summary>
    public static readonly FixedTime Exploration = new FixedTime(new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero));

    public static readonly DateTimeOffset ExplorationEnd = new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }
}