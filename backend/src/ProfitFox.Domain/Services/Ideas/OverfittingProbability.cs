using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The best rule of a grid over some blocks, and the probability of backtest overfitting of the grid by combinatorially symmetric cross
///     validation (Bailey, Borwein, López de Prado, Zhu; decision 5 of check-idea-exit-rules-out-of-sample). A rule takes part in a half with
///     30 entries or more in it, as a bucket has enough data; no randomness.
/// </summary>
public static class OverfittingProbability
{
    /// <summary>
    ///     The rule with the highest expectancy per entry over <paramref name="blocks" /> among those with 30 entries or more there, the first in
    ///     the order of the grid on a tie; none without one.
    /// </summary>
    public static int? Best(IReadOnlyList<BlockSums[]> rules, IReadOnlyList<int> blocks)
    {
        return Best(Taking(rules, blocks));
    }

    /// <summary>
    ///     Each of the 20 ways to take 3 of the 6 blocks as the first half and the other 3 as the second: the rules with 30 entries in both halves
    ///     take part, and the best of the first half gets the place r = 1 + the rules strictly worse on the second. The split is an overfit when
    ///     r / (k + 1) ≤ 1/2 of its k rules; one with fewer than two rules does not count. None without a split that counts.
    /// </summary>
    public static Overfitting? Of(IReadOnlyList<BlockSums[]> rules)
    {
        int splits = 0;
        int overfit = 0;

        foreach (int[] first in Halves())
        {
            int[] second = [.. Enumerable.Range(0, ExitRuleGridPass.Blocks).Except(first)];
            decimal?[] ins = Taking(rules, first);
            decimal?[] outs = Taking(rules, second);
            int[] taking = [.. Enumerable.Range(0, rules.Count).Where(rule => ins[rule] is not null && outs[rule] is not null)];

            if (taking.Length < 2)
            {
                continue;
            }

            int best = Best([.. Enumerable.Range(0, rules.Count).Select(rule => taking.Contains(rule) ? ins[rule] : null)])!.Value;
            int place = 1 + taking.Count(rule => outs[rule] < outs[best]);
            splits++;

            if (2 * place <= taking.Length + 1)
            {
                overfit++;
            }
        }

        return splits == 0 ? null : new Overfitting(splits, overfit);
    }

    private static int? Best(decimal?[] expectancies)
    {
        int? best = null;

        for (int rule = 0; rule < expectancies.Length; rule++)
        {
            if (expectancies[rule] is { } expectancy && (best is null || expectancy > expectancies[best.Value]))
            {
                best = rule;
            }
        }

        return best;
    }

    // The expectancy per entry of each rule over the blocks; none under 30 entries.
    private static decimal?[] Taking(IReadOnlyList<BlockSums[]> rules, IReadOnlyList<int> blocks)
    {
        return
        [
            .. rules.Select(rule =>
            {
                int entries = blocks.Sum(block => rule[block].Entries);

                return entries < BucketStatisticsCalculator.MinimumCases ? (decimal?)null : blocks.Sum(block => rule[block].TotalR) / entries;
            })
        ];
    }

    // The sets of 3 of the 6 blocks in lexicographic order.
    private static IEnumerable<int[]> Halves()
    {
        for (int a = 0; a < ExitRuleGridPass.Blocks; a++)
        {
            for (int b = a + 1; b < ExitRuleGridPass.Blocks; b++)
            {
                for (int c = b + 1; c < ExitRuleGridPass.Blocks; c++)
                {
                    yield return [a, b, c];
                }
            }
        }
    }
}