using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The best rule of a grid on the selection, and the probability of backtest overfitting of the grid by combinatorially symmetric cross
///     validation (Bailey, Borwein, López de Prado, Zhu; decisions 5, 9 and 10 of check-idea-exit-rules-out-of-sample). A rule takes part with
///     30 entries or more, as a bucket has enough data; no randomness.
/// </summary>
public static class OverfittingProbability
{
    /// <summary>The rule with the highest expectancy per entry among those with 30 entries or more, the first in the order of the grid on a tie; none without one.</summary>
    public static int? Best(IReadOnlyList<BlockSums> sums)
    {
        return Best([.. sums.Select(Expectancy)]);
    }

    /// <summary>
    ///     Each of the 20 ways to take 3 of the 6 blocks as the first half and the other 3 as the second. Rules with the same sums in every block
    ///     are one rule, the first of them. The first half leaves out the tail of a block that a block of the second half follows, so none of its
    ///     signals closes in the second; the second half counts whole. The rules with 30 entries in both halves take part, and the best of the
    ///     first half gets the place r = 1 + the rules strictly worse on the second. The split is an overfit when r / (k + 1) ≤ 1/2 of its k
    ///     rules; one with fewer than two rules does not count. None without a split that counts.
    /// </summary>
    public static Overfitting? Of(IReadOnlyList<RuleSums> rules)
    {
        RuleSums[] distinct = [.. rules.DistinctBy(Whole)];
        int splits = 0;
        int overfit = 0;

        foreach (int[] first in Halves())
        {
            decimal?[] ins = [.. distinct.Select(rule => Expectancy(First(rule, first)))];
            decimal?[] outs = [.. distinct.Select(rule => Expectancy(Second(rule, first)))];
            int[] taking = [.. Enumerable.Range(0, distinct.Length).Where(rule => ins[rule] is not null && outs[rule] is not null)];

            if (taking.Length < 2)
            {
                continue;
            }

            int best = Best([.. Enumerable.Range(0, distinct.Length).Select(rule => taking.Contains(rule) ? ins[rule] : null)])!.Value;
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

    // The sums of every block, body and tail together, compared by value: twins have the same.
    private static (BlockSums, BlockSums, BlockSums, BlockSums, BlockSums, BlockSums) Whole(RuleSums rule)
    {
        BlockSums Of(int block) => Add(rule.Bodies[block], rule.Tails[block]);

        return (Of(0), Of(1), Of(2), Of(3), Of(4), Of(5));
    }

    private static BlockSums First(RuleSums rule, int[] half)
    {
        BlockSums sums = default;

        foreach (int block in half)
        {
            sums = Add(sums, rule.Bodies[block]);

            if (block == ExitRuleGridPass.Blocks - 1 || half.Contains(block + 1))
            {
                sums = Add(sums, rule.Tails[block]);
            }
        }

        return sums;
    }

    private static BlockSums Second(RuleSums rule, int[] first)
    {
        BlockSums sums = default;

        foreach (int block in Enumerable.Range(0, ExitRuleGridPass.Blocks).Except(first))
        {
            sums = Add(Add(sums, rule.Bodies[block]), rule.Tails[block]);
        }

        return sums;
    }

    private static BlockSums Add(BlockSums left, BlockSums right)
    {
        return new BlockSums(left.Entries + right.Entries, left.TotalR + right.TotalR);
    }

    // The expectancy per entry; none under 30 entries.
    private static decimal? Expectancy(BlockSums sums)
    {
        return sums.Entries < BucketStatisticsCalculator.MinimumCases ? null : sums.TotalR / sums.Entries;
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