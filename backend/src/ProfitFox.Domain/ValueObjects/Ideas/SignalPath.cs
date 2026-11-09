using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The plan of a signal of the replay and what the price did after it (decision 1). The signal fired at the end of the minute before
///     <see cref="FiredAt" />. <see cref="Entry" /> is the price it entered at — the open of the minute after it fired at the market, the open of a
///     stop entry that gapped — or the planned one without an entry; <see cref="Target" /> is the zone of «Уровни», none for a target in R.
///     The times are minutes to the end of the minute that touched: <see cref="EntryAfter" /> and <see cref="StopBeforeEntryAfter" /> from the
///     firing, the others from the open of the minute of the entry. <see cref="LadderAfter" /> holds the first touches of +0.5 … +5 R before the
///     stop, <see cref="Closes" /> the closes at the ends of the candles of the grid of <see cref="Interval" /> from the entry before the stop,
///     and the path is known up to <see cref="TrackedUntil" />.
/// </summary>
public sealed record SignalPath(
    IdeaType Type,
    IdeaSide Side,
    Interval Interval,
    DateTimeOffset FiredAt,
    decimal Entry,
    decimal Stop,
    decimal? Target,
    decimal Tick,
    int? EntryAfter,
    int? StopBeforeEntryAfter,
    int? StopAfter,
    IReadOnlyList<int?> LadderAfter,
    int? TargetAfter,
    IReadOnlyList<decimal> Closes,
    DateTimeOffset TrackedUntil)
{
    /// <summary>The levels of the ladder: +0.5 … +5 R by 0.5 R.</summary>
    public static readonly IReadOnlyList<decimal> Ladder = [.. Enumerable.Range(1, 10).Select(step => step / 2m)];

    /// <summary>How long a path waits for its entry, and how long it goes on after it.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(7);

    public int CandleMinutes => (int)(Interval.CandleEnd(DateTimeOffset.UnixEpoch) - DateTimeOffset.UnixEpoch).TotalMinutes;

    /// <summary>The open of the minute of the entry.</summary>
    public DateTimeOffset? EnteredAt => EntryAfter is { } after ? FiredAt.AddMinutes(after - 1) : null;

    public bool IsMarket => Type is not (IdeaType.Pullback or IdeaType.StopEntry);

    /// <summary>
    ///     Whether the path goes on: it ends at the stop, before or after the entry, 7 days after the entry, at the minute that opens 7 days after
    ///     the firing without one — which ends a window of 7 days — or at the first minute of a market signal that did not enter.
    /// </summary>
    public bool IsOpen => IsOpenAt(TrackedUntil);

    /// <summary>Whether the path would go on were it tracked until <paramref name="trackedUntil" />.</summary>
    public bool IsOpenAt(DateTimeOffset trackedUntil)
    {
        return StopAfter is null && StopBeforeEntryAfter is null && (EnteredAt is { } entered
            ? trackedUntil < entered + Horizon
            : IsMarket ? trackedUntil == FiredAt : trackedUntil <= FiredAt + Horizon);
    }

    public static SignalPath Fired(IdeaPlan plan, Interval interval, DateTimeOffset firedAt, decimal tick)
    {
        return new SignalPath(
            plan.Type,
            plan.Side,
            interval,
            firedAt,
            plan.Entry,
            plan.Stop,
            plan.Type == IdeaType.Levels ? plan.Target : null,
            tick,
            null,
            null,
            null,
            [.. Ladder.Select(_ => (int?)null)],
            null,
            [],
            firedAt);
    }
}