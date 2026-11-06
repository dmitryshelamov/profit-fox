using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas;

/// <summary>
///     The out-of-sample check of a candidate exit rule from the signals of its type × interval over 90 days, all setups, pairs and categories
///     together (decisions 1–5 of check-idea-exit-rules-out-of-sample): the grid in one pass for the best rule of the selection and the
///     probability of overfitting, and the numbers of the candidate and of that best rule by their own passes, clustered by the hour they fired.
/// </summary>
public static class ExitRuleOutOfSamples
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(90);
    private static readonly TimeSpan Checked = TimeSpan.FromDays(30);

    // The blocks of 15 days from 90 to 30 days ago.
    private static readonly int[] Selection = [0, 1, 2, 3];

    /// <param name="signals">The signals of one type × interval fired in the last 90 days, in the order of their key and firing.</param>
    /// <param name="candidate">The rule to check; the grid takes its window.</param>
    /// <param name="fees">The fees of a category.</param>
    /// <param name="now">The moment the window ends at.</param>
    public static ExitRuleOutOfSample Compute(IReadOnlyList<IdeaSignal> signals, IdeaExitRule candidate, Func<MarketCategory, FeeRates> fees, DateTimeOffset now)
    {
        (FeeRates Fees, IReadOnlyList<SignalPath> Paths)[] keys =
        [
            .. signals.GroupBy(signal => signal.Key).Select(key => (fees(key.Key.Instrument.Category), (IReadOnlyList<SignalPath>)[.. key.Select(signal => signal.Path)]))
        ];
        DateTimeOffset from = now - Window;
        DateTimeOffset checkFrom = now - Checked;
        IReadOnlyList<IdeaExitRule> rules = ExitRuleGridPass.Rules(candidate);
        BlockSums[][] sums = ExitRuleGridPass.Sums(keys, rules, from);
        ExitRuleBest? best = OverfittingProbability.Best(sums, Selection) is { } index
            ? new ExitRuleBest(rules[index], Parts(keys, rules[index], checkFrom))
            : null;

        return new ExitRuleOutOfSample(from, checkFrom, now, rules.Count, Parts(keys, candidate, checkFrom), best, OverfittingProbability.Of(sums));
    }

    private static ExitRuleParts Parts((FeeRates Fees, IReadOnlyList<SignalPath> Paths)[] keys, IdeaExitRule rule, DateTimeOffset checkFrom)
    {
        var selection = new ClusteredSums();
        var check = new ClusteredSums();

        foreach ((FeeRates rates, IReadOnlyList<SignalPath> paths) in keys)
        {
            foreach ((SignalPath path, IdeaTrack outcome) in ExitOutcomes.Counted(paths, rule))
            {
                if (CaseResult.R(outcome, rates) is { } result)
                {
                    (path.FiredAt < checkFrom ? selection : check).Add(path.FiredAt, outcome.Status, result);
                }
            }
        }

        return new ExitRuleParts(Statistics(selection), Statistics(check));
    }

    private static BucketStatistics? Statistics(ClusteredSums sums)
    {
        return sums.Cases == 0 ? null : sums.Statistics();
    }
}