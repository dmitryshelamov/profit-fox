using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The outcome of a signal by an exit rule, from its path alone (decision 3), as <see cref="IdeaTracker" /> would close the idea: without an
///     entry within the window «Истекла» at its end, or at the stop of a stop entry before it; after an entry «Цель» at the first touch of the
///     target, «Стоп» at the stop, in the lifetime from the entry, «Истекла» at its end by the close then. None while the path has not decided it.
///     <see cref="CaseResult" /> gives its result.
/// </summary>
public static class ExitOutcomes
{
    public static IdeaTrack? Of(SignalPath path, IdeaExitRule rule)
    {
        int candle = path.CandleMinutes;
        var plan = new IdeaPlan(path.Type, path.Side, path.Entry, path.Stop, path.Target ?? SignalPathTracker.Level(path, rule.TargetR!.Value));
        var fired = new IdeaTrack(plan, path.FiredAt, candle, rule.LifeCandles, rule.TargetR, rule.EntryCandles, IdeaStatus.AwaitingEntry, path.FiredAt, Tick: path.Tick);
        // A market signal has its one minute to enter.
        int window = path.IsMarket ? 1 : rule.EntryCandles!.Value * candle;

        if (path.EntryAfter is not { } entryAfter || entryAfter > window)
        {
            DateTimeOffset deadline = path.FiredAt.AddMinutes(path.IsMarket ? 0 : window);

            if (path.StopBeforeEntryAfter is { } stopBefore && stopBefore <= window)
            {
                return fired with { Status = IdeaStatus.Expired, StatusAt = path.FiredAt.AddMinutes(stopBefore - 1) };
            }

            // Like the idea, the path knows its window is over at the minute that opens at its end.
            return path.TrackedUntil > deadline ? fired with { Status = IdeaStatus.Expired, StatusAt = deadline } : null;
        }

        DateTimeOffset entered = path.EnteredAt!.Value;
        int life = rule.LifeCandles * candle;
        IdeaTrack active = fired with { Status = IdeaStatus.Active, EnteredAt = entered };
        int? target = path.Target is null ? path.LadderAfter[LadderIndex(rule.TargetR!.Value)] : path.TargetAfter;

        if (target <= life)
        {
            return active with { Status = IdeaStatus.Target, StatusAt = entered.AddMinutes(target.Value - 1), ExitPrice = plan.Target };
        }

        if (path.StopAfter <= life)
        {
            return active with { Status = IdeaStatus.Stop, StatusAt = entered.AddMinutes(path.StopAfter.Value - 1), ExitPrice = plan.Stop };
        }

        DateTimeOffset end = entered.AddMinutes(life);

        return path.TrackedUntil >= end
            ? active with { Status = IdeaStatus.Expired, StatusAt = end, ExitPrice = path.Closes[GridIndex(path, rule.LifeCandles)] }
            : null;
    }

    /// <summary>
    ///     The signals of one key that count, in the order of firing, with their outcomes by the rule (requirement "Повтор пока идея открыта"): as
    ///     one open idea at a time, a signal counts when it fired no earlier than the end of the minute that closed the one counted before it. A
    ///     signal without an outcome yet ends the ones that count: the ones after it may still fall inside it.
    /// </summary>
    public static IEnumerable<(SignalPath Path, IdeaTrack Outcome)> Counted(IEnumerable<SignalPath> signals, IdeaExitRule rule)
    {
        DateTimeOffset free = DateTimeOffset.MinValue;

        foreach (SignalPath path in signals)
        {
            if (path.FiredAt < free)
            {
                continue;
            }

            if (Of(path, rule) is not { } outcome)
            {
                yield break;
            }

            // An expiry after the entry is at the end of the last minute of the lifetime; the other outcomes at the open of their minute.
            free = outcome is { Status: IdeaStatus.Expired, EnteredAt: not null } ? outcome.StatusAt : outcome.StatusAt.AddMinutes(1);

            yield return (path, outcome);
        }
    }

    private static int GridIndex(SignalPath path, int life)
    {
        IReadOnlyList<int> grid = ExitRuleGrid.Of(path.Interval);
        int index = 0;

        while (grid[index] != life)
        {
            index++;
        }

        return index;
    }

    private static int LadderIndex(decimal targetR)
    {
        return (int)(targetR / 0.5m) - 1;
    }
}