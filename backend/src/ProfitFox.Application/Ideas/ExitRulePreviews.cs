using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas;

/// <summary>
///     The preview of a candidate exit rule from the signals of its type × interval over 90 days, all setups, pairs and categories together
///     (decision 8). Every rule takes its own pass: which signals count depends on the rule. A signal without an entry counts as 0 R per signal;
///     a target that does not pay the fees breaks even at all of the entries.
/// </summary>
public static class ExitRulePreviews
{
    private static readonly int[] Days = [90, 30];

    /// <param name="signals">The signals of one type × interval fired in the last 90 days, in the order of their key and firing.</param>
    /// <param name="current">The rule in force.</param>
    /// <param name="candidate">The rule to preview.</param>
    /// <param name="fees">The fees of a category.</param>
    /// <param name="now">The moment the windows end at.</param>
    public static ExitRulePreview Compute(
        IReadOnlyList<IdeaSignal> signals,
        IdeaExitRule current,
        IdeaExitRule candidate,
        Func<MarketCategory, FeeRates> fees,
        DateTimeOffset now)
    {
        // One group of paths per key, read by every pass.
        (FeeRates Fees, SignalPath[] Paths)[] keys = [.. signals.GroupBy(signal => signal.Key).Select(key => (fees(key.Key.Instrument.Category), key.Select(signal => signal.Path).ToArray()))];
        IReadOnlyList<Counted> now90 = Count(keys, current);
        IReadOnlyList<Counted> candidate90 = Count(keys, candidate);
        ExitRuleWindow[] windows =
        [
            .. Days.Select(days => new ExitRuleWindow(
                Statistics(now90.Where(counted => counted.FiredAt >= now.AddDays(-days))),
                Statistics(candidate90.Where(counted => counted.FiredAt >= now.AddDays(-days)))))
        ];
        ExitRuleLife[] lives =
        [
            .. ExitRuleGrid.Of(candidate.Interval).Select(life =>
            {
                BucketStatistics? statistics = Statistics(Count(keys, candidate with { LifeCandles = life }));

                return new ExitRuleLife(life, statistics?.Expectancy, statistics?.SignalExpectancy);
            })
        ];

        return new ExitRulePreview(windows[0], windows[1], lives, Mfe(candidate90, candidate));
    }

    // The signals that count by the rule, with their results; none without an entry.
    private static List<Counted> Count((FeeRates Fees, SignalPath[] Paths)[] keys, IdeaExitRule rule)
    {
        var counted = new List<Counted>();

        foreach ((FeeRates rates, SignalPath[] paths) in keys)
        {
            foreach ((SignalPath path, IdeaTrack outcome) in ExitOutcomes.Counted(paths, rule))
            {
                counted.Add(new Counted(path, outcome, CaseResult.R(outcome, rates), rates));
            }
        }

        return counted;
    }

    private static BucketStatistics? Statistics(IEnumerable<Counted> counted)
    {
        Counted[] all = [.. counted];
        decimal[] results = [.. all.Select(item => item.Result).OfType<decimal>()];

        if (results.Length == 0)
        {
            return null;
        }

        Counted[] entered = [.. all.Where(item => item.Result is not null)];

        return BucketStatisticsCalculator.Compute(
            entered.Count(item => item.Outcome.Status == IdeaStatus.Target),
            entered.Count(item => item.Outcome.Status == IdeaStatus.Stop),
            entered.Count(item => item.Outcome.Status == IdeaStatus.Expired),
            results.Sum(),
            results.Sum(result => result * result),
            all.Length);
    }

    private static ExitRuleMfe[] Mfe(IReadOnlyList<Counted> counted, IdeaExitRule rule)
    {
        Counted[] entered = [.. counted.Where(item => item.Result is not null)];

        if (entered.Length == 0)
        {
            return [];
        }

        int life = rule.LifeCandles * entered[0].Path.CandleMinutes;

        return
        [
            .. SignalPath.Ladder.Select((r, index) => new ExitRuleMfe(
                r,
                (decimal)entered.Count(item => item.Path.LadderAfter[index] <= life) / entered.Length,
                entered.Average(item => BreakEven(item, r))))
        ];
    }

    private static decimal BreakEven(Counted item, decimal r)
    {
        SignalPath path = item.Path;
        var plan = new IdeaPlan(path.Type, path.Side, path.Entry, path.Stop, SignalPathTracker.Level(path, r));

        return IdeaMoney.PerUsdt(plan, item.Fees).BreakEven ?? 1m;
    }

    private sealed record Counted(SignalPath Path, IdeaTrack Outcome, decimal? Result, FeeRates Fees)
    {
        public DateTimeOffset FiredAt => Path.FiredAt;
    }
}