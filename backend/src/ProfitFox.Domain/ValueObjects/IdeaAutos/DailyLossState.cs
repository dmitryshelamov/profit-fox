namespace ProfitFox.Domain.ValueObjects.IdeaAutos;

/// <summary>
///     The money of the closed real trades of a day in USDT after the fees against the <paramref name="Limit" /> of the daily stop (none while the
///     guard is off) and, when the stop holds, when it was reached and when the autos enter again.
/// </summary>
public sealed record DailyLossState(decimal Sum, decimal? Limit, DateTimeOffset? TriggeredAt, DateTimeOffset? ResumesAt)
{
    public bool Holds => TriggeredAt is not null;
}