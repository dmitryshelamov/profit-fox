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

        DateTimeOffset end = minute.OpenTime.AddMinutes(1);
        int sinceFired = (int)(end - path.FiredAt).TotalMinutes;

        if (path.EntryAfter is null)
        {
            return Enter(path with { TrackedUntil = end }, minute, sinceFired);
        }

        return Exit(path with { TrackedUntil = end }, minute, (int)(end - path.EnteredAt!.Value).TotalMinutes);
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

        for (int index = 0; index < SignalPath.Ladder.Count; index++)
        {
            if (path.LadderAfter[index] is null && ahead * (best - Level(path, SignalPath.Ladder[index])) >= 0)
            {
                ladder ??= [.. path.LadderAfter];
                ladder[index] = sinceEntry;
            }
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