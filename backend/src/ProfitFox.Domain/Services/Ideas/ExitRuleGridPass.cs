using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>The grid of exit rules the out-of-sample check of a candidate goes through (decision 3 of check-idea-exit-rules-out-of-sample).</summary>
public static class ExitRuleGridPass
{
    /// <summary>The blocks of 15 days the 90 days of the check are cut into.</summary>
    public const int Blocks = 6;

    private const int Parts = 8;

    private static readonly TimeSpan BlockLength = TimeSpan.FromDays(15);
    private static readonly decimal[] Targets = [.. SignalPath.Ladder.Where(r => r >= 1m)];

    /// <summary>
    ///     Every lifetime of the grid of the interval by every target from 1 to 5 R, the lifetimes first, at the window of the candidate;
    ///     the lifetimes alone for «Уровни».
    /// </summary>
    public static IReadOnlyList<IdeaExitRule> Rules(IdeaExitRule candidate)
    {
        decimal?[] targets = IdeaExitRule.HasTarget(candidate.Type) ? [.. Targets.Select(target => (decimal?)target)] : [null];

        return
        [
            .. ExitRuleGrid.Of(candidate.Interval).SelectMany(life => targets.Select(target =>
                new IdeaExitRule(candidate.Type, candidate.Interval, life, target, candidate.EntryCandles, null)))
        ];
    }

    /// <summary>
    ///     The entries of every rule and the total of their results in R in each block of 15 days from <paramref name="from" />, by the time
    ///     their signals fired. One step a signal for all the rules: each rule keeps when its last counted signal closed and whether a path
    ///     left an outcome undecided, as <see cref="ExitOutcomes.Counted" /> does; the decision is in minutes of the path, as
    ///     <see cref="ExitOutcomes.Of" /> takes it, and a result is <see cref="CaseResult.R" /> of that outcome, once a signal for each target,
    ///     the stop and each lifetime that expires it.
    /// </summary>
    /// <param name="keys">The signals of each key in the order of firing, with the fees of its category; none fired before <paramref name="from" />.</param>
    /// <param name="rules">Rules of one type × interval.</param>
    /// <param name="from">The start of the first block.</param>
    public static BlockSums[][] Sums(IReadOnlyList<(FeeRates Fees, IReadOnlyList<SignalPath> Paths)> keys, IReadOnlyList<IdeaExitRule> rules, DateTimeOffset from)
    {
        Minutes[] minutes = [.. rules.Select(Minutes.Of)];
        var parts = new BlockSums[Parts][][];

        // Key k goes to part k mod 8 and the parts add up in their order, so the sums do not depend on the cores or their scheduling.
        Parallel.For(0, Parts, part =>
        {
            BlockSums[][] sums = Empty(rules.Count);

            for (int key = part; key < keys.Count; key += Parts)
            {
                Walk(keys[key].Paths, keys[key].Fees, rules, minutes, from, sums);
            }

            parts[part] = sums;
        });

        BlockSums[][] total = Empty(rules.Count);

        foreach (BlockSums[][] part in parts)
        {
            for (int rule = 0; rule < rules.Count; rule++)
            {
                for (int block = 0; block < Blocks; block++)
                {
                    BlockSums before = total[rule][block];
                    total[rule][block] = new BlockSums(before.Entries + part[rule][block].Entries, before.TotalR + part[rule][block].TotalR);
                }
            }
        }

        return total;
    }

    private static BlockSums[][] Empty(int rules)
    {
        return [.. Enumerable.Range(0, rules).Select(_ => new BlockSums[Blocks])];
    }

    private static void Walk(IReadOnlyList<SignalPath> paths, FeeRates fees, IReadOnlyList<IdeaExitRule> rules, Minutes[] minutes, DateTimeOffset from, BlockSums[][] sums)
    {
        var free = new DateTimeOffset[rules.Count];
        var undecided = new bool[rules.Count];
        var results = new decimal?[Minutes.Slots];

        foreach (SignalPath path in paths)
        {
            Array.Clear(results);
            int block = (int)((path.FiredAt - from) / BlockLength);

            for (int rule = 0; rule < rules.Count; rule++)
            {
                if (undecided[rule] || path.FiredAt < free[rule])
                {
                    continue;
                }

                if (Close(path, minutes[rule]) is not { } close)
                {
                    undecided[rule] = true;
                    continue;
                }

                free[rule] = close.Free;

                if (close.Slot is { } slot)
                {
                    decimal result = results[slot] ??= CaseResult.R(ExitOutcomes.Of(path, rules[rule])!, fees)!.Value;
                    BlockSums before = sums[rule][block];
                    sums[rule][block] = new BlockSums(before.Entries + 1, before.TotalR + result);
                }
            }
        }
    }

    // When the outcome of the path by the rule frees its key, and the slot of its result; no slot without an entry, none while undecided.
    private static (DateTimeOffset Free, int? Slot)? Close(SignalPath path, Minutes rule)
    {
        int window = path.IsMarket ? 1 : rule.Window;

        if (path.EntryAfter is not { } entryAfter || entryAfter > window)
        {
            if (path.StopBeforeEntryAfter is { } stopBefore && stopBefore <= window)
            {
                return (path.FiredAt.AddMinutes(stopBefore), null);
            }

            DateTimeOffset deadline = path.FiredAt.AddMinutes(path.IsMarket ? 0 : window);

            return path.TrackedUntil > deadline ? (deadline.AddMinutes(1), null) : null;
        }

        DateTimeOffset entered = path.EnteredAt!.Value;
        int? target = path.Target is null ? path.LadderAfter[rule.Ladder] : path.TargetAfter;

        if (target <= rule.Life)
        {
            return (entered.AddMinutes(target.Value), path.Target is null ? rule.Ladder : Minutes.Zone);
        }

        if (path.StopAfter <= rule.Life)
        {
            return (entered.AddMinutes(path.StopAfter.Value), Minutes.Stop);
        }

        DateTimeOffset end = entered.AddMinutes(rule.Life);

        return path.TrackedUntil >= end ? (end, Minutes.Expired + rule.Grid) : null;
    }

    // A rule in minutes of its interval, and the slots of the results of a signal: the levels of the ladder, the zone, the stop and the
    // lifetimes of the grid.
    private readonly record struct Minutes(int Life, int Window, int Ladder, int Grid)
    {
        public const int Zone = 10;
        public const int Stop = 11;
        public const int Expired = 12;
        // The grid of a minute is the longest.
        public static readonly int Slots = Expired + ExitRuleGrid.Of(Interval.OneMinute).Count;

        public static Minutes Of(IdeaExitRule rule)
        {
            IReadOnlyList<int> grid = ExitRuleGrid.Of(rule.Interval);
            int candle = (int)(rule.Interval.CandleEnd(DateTimeOffset.UnixEpoch) - DateTimeOffset.UnixEpoch).TotalMinutes;
            int ladder = rule.TargetR is { } target ? (int)(target / 0.5m) - 1 : 0;

            return new Minutes(rule.LifeCandles * candle, (rule.EntryCandles ?? 0) * candle, ladder, grid.Count(life => life < rule.LifeCandles));
        }
    }
}