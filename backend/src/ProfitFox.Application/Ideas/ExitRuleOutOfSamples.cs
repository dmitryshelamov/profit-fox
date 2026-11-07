using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas;

/// <summary>
///     The out-of-sample check of a candidate exit rule from the signals of its type × interval over 90 days, all setups, pairs and categories
///     together (decisions 1–10 of check-idea-exit-rules-out-of-sample): the grid in one pass for the best rule of the selection and the
///     probability of overfitting, and the numbers of the candidate and of that best rule by their own passes, clustered by the hour they fired.
/// </summary>
public static class ExitRuleOutOfSamples
{
    /// <param name="signals">The signals of one type × interval fired in the last 90 days, in the order of their key and firing.</param>
    /// <param name="candidate">The rule to check; the grid takes its window.</param>
    /// <param name="fees">The fees of a category.</param>
    /// <param name="now">The moment the check is made at.</param>
    /// <param name="cancellationToken">Stops the pass of the grid at the next key.</param>
    public static ExitRuleOutOfSample Compute(
        IReadOnlyList<IdeaSignal> signals,
        IdeaExitRule candidate,
        Func<MarketCategory, FeeRates> fees,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        (FeeRates Fees, IReadOnlyList<SignalPath> Paths)[] keys =
        [
            .. signals.GroupBy(signal => signal.Key).Select(key => (fees(key.Key.Instrument.Category), (IReadOnlyList<SignalPath>)[.. key.Select(signal => signal.Path)]))
        ];
        var window = OutOfSampleWindow.Of(candidate, now);
        IReadOnlyList<IdeaExitRule> rules = ExitRuleGridPass.Rules(candidate);
        RuleSums[] sums = ExitRuleGridPass.Sums(keys, rules, window, cancellationToken);
        ExitRuleBest? best = OverfittingProbability.Best([.. sums.Select(rule => rule.Selection)]) is { } index
            ? new ExitRuleBest(rules[index], Parts(keys, rules[index], window))
            : null;

        return new ExitRuleOutOfSample(window, rules.Count, Parts(keys, candidate, window), best, OverfittingProbability.Of(sums));
    }

    private static ExitRuleParts Parts((FeeRates Fees, IReadOnlyList<SignalPath> Paths)[] keys, IdeaExitRule rule, OutOfSampleWindow window)
    {
        TimeSpan horizon = OutOfSampleWindow.Horizon(rule);
        var selection = new ClusteredSums();
        var check = new ClusteredSums();

        foreach ((FeeRates rates, IReadOnlyList<SignalPath> paths) in keys)
        {
            foreach ((SignalPath path, IdeaTrack outcome) in ExitOutcomes.Counted(paths, rule))
            {
                if (CaseResult.R(outcome, rates) is not { } result)
                {
                    continue;
                }

                switch (window.PartOf(horizon, path.FiredAt))
                {
                    case OutOfSamplePart.Selection:
                        selection.Add(path.FiredAt, outcome.Status, result);
                        break;
                    case OutOfSamplePart.Check:
                        check.Add(path.FiredAt, outcome.Status, result);
                        break;
                }
            }
        }

        return new ExitRuleParts(window.CheckFrom - horizon, Statistics(selection), Statistics(check));
    }

    private static BucketStatistics? Statistics(ClusteredSums sums)
    {
        return sums.Cases == 0 ? null : sums.Statistics();
    }
}