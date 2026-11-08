using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     Steps the path of a signal by the next closed minute (decision 3), by the touches of <see cref="IdeaTracker" />: a market signal enters
///     at the open of the minute after it fired unless that is beyond its stop or the zone of «Уровни»; a pullback enters when a minute touches
///     its limit, a stop entry when a minute reaches its stop order, at the open of a minute that opened beyond it. In the minute of a fill by a
///     limit or a stop order only the stop counts. A level counts in a minute that did not reach the stop. The levels of the ladder go on the
///     price step towards the entry, as a target in R does.
/// </summary>
public static class SignalPathTracker
{
    public static SignalPath Step(SignalPath path, IdeaCandle minute)
    {
        if (!path.IsOpen)
        {
            return path;
        }

        return Move(path, minute) with { TrackedUntil = minute.OpenTime.AddMinutes(1) };
    }

    /// <summary>
    ///     Steps a path tracked up to the open of <paramref name="minute" />, whatever its <see cref="SignalPath.TrackedUntil" /> says, and gives
    ///     it back as it is when the minute touched nothing on it (decision 1 of speed-up-ideas-replay: the replay steps every open path every
    ///     minute, and a new record for each was a fifth of a turn). A path the minute touched is tracked until its end; a path that had ended by
    ///     time at the open of the minute is tracked until that open, as the steps leave it. Once <see cref="SignalPath.TrackedUntil" /> is set
    ///     to where the tracking stopped, the path is the one the steps give.
    /// </summary>
    public static SignalPath StepLazily(SignalPath path, IdeaCandle minute)
    {
        if (!path.IsOpen)
        {
            return path;
        }

        DateTimeOffset until = minute.OpenTime;

        if (!path.IsOpenAt(until))
        {
            return path with { TrackedUntil = until };
        }

        SignalPath moved = Move(path, minute);

        return ReferenceEquals(moved, path) ? path : moved with { TrackedUntil = until.AddMinutes(1) };
    }

    // The entry or the exit of a path on the minute, the tracking left to the caller.
    private static SignalPath Move(SignalPath path, IdeaCandle minute)
    {
        DateTimeOffset end = minute.OpenTime.AddMinutes(1);
        int sinceFired = (int)(end - path.FiredAt).TotalMinutes;

        if (path.EntryAfter is null)
        {
            return Enter(path, minute, sinceFired);
        }

        return Exit(path, minute, (int)(end - path.EnteredAt!.Value).TotalMinutes);
    }

    private static SignalPath Enter(SignalPath path, IdeaCandle minute, int sinceFired)
    {
        decimal ahead = Ahead(path);

        if (path.IsMarket)
        {
            // Beyond its stop a signal has no risk to measure, beyond the zone of «Уровни» nothing to gain.
            if (ahead * (minute.Open - path.Stop) <= 0 || (path.Target is { } zone && ahead * (zone - minute.Open) <= 0))
            {
                return path;
            }

            return Exit(path with { Entry = minute.Open, EntryAfter = sinceFired }, minute, 1);
        }

        // The minute that opens 7 days after the firing only closes the longest window.
        if (sinceFired > SignalPath.Horizon.TotalMinutes)
        {
            return path;
        }

        if (path.Type == IdeaType.Pullback)
        {
            return ahead * (Worst(path, minute) - path.Entry) > 0 ? path : Filled(path with { EntryAfter = sinceFired }, minute);
        }

        if (ahead * (Best(path, minute) - path.Entry) < 0)
        {
            return ahead * (Worst(path, minute) - path.Stop) <= 0 ? path with { StopBeforeEntryAfter = sinceFired } : path;
        }

        // A minute that opened beyond the stop order fills at its open.
        decimal entry = ahead * (minute.Open - path.Entry) > 0 ? minute.Open : path.Entry;

        return Filled(path with { Entry = entry, EntryAfter = sinceFired }, minute);
    }

    // Within the minute of the fill the levels may have come before it; only the stop counts.
    private static SignalPath Filled(SignalPath path, IdeaCandle minute)
    {
        return Ahead(path) * (Worst(path, minute) - path.Stop) <= 0 ? path with { StopAfter = 1 } : path;
    }

    private static SignalPath Exit(SignalPath path, IdeaCandle minute, int sinceEntry)
    {
        decimal ahead = Ahead(path);

        if (ahead * (Worst(path, minute) - path.Stop) <= 0)
        {
            return path with { StopAfter = sinceEntry };
        }

        decimal best = Best(path, minute);
        int?[]? ladder = null;
        // A minute touches every level up to its best, so the touched ones are the first of the ladder: the next one is the first untouched.
        int next = 0;

        while (next < SignalPath.Ladder.Count && path.LadderAfter[next] is not null)
        {
            next++;
        }

        for (; next < SignalPath.Ladder.Count && ahead * (best - Level(path, SignalPath.Ladder[next])) >= 0; next++)
        {
            ladder ??= [.. path.LadderAfter];
            ladder[next] = sinceEntry;
        }

        SignalPath touched = ladder is null ? path : path with { LadderAfter = ladder };

        if (touched is { TargetAfter: null, Target: { } zone } && ahead * (best - zone) >= 0)
        {
            touched = touched with { TargetAfter = sinceEntry };
        }

        IReadOnlyList<int> grid = ExitRuleGrid.Of(path.Interval);

        return touched.Closes.Count < grid.Count && grid[touched.Closes.Count] * path.CandleMinutes == sinceEntry
            ? touched with { Closes = [.. touched.Closes, minute.Close] }
            : touched;
    }

    /// <summary>The level of <paramref name="r" /> risks from the entry, on the price step towards it.</summary>
    public static decimal Level(SignalPath path, decimal r)
    {
        return PriceStep.Round(path.Entry + (r * (path.Entry - path.Stop)), path.Tick, path.Side == IdeaSide.Long);
    }

    private static decimal Ahead(SignalPath path)
    {
        return path.Side == IdeaSide.Long ? 1m : -1m;
    }

    private static decimal Worst(SignalPath path, IdeaCandle minute)
    {
        return path.Side == IdeaSide.Long ? minute.Low : minute.High;
    }

    private static decimal Best(SignalPath path, IdeaCandle minute)
    {
        return path.Side == IdeaSide.Long ? minute.High : minute.Low;
    }
}