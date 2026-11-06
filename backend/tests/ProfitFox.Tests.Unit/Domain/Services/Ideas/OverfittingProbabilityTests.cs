using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Requirements "Лучшее правило сетки", "Разбиения проверки" and "Вероятность подгонки" of check-idea-exit-rules-out-of-sample on reference
///     matrices: the entries and the total R of each rule of a grid in the 6 blocks of 15 days.
/// </summary>
public class OverfittingProbabilityTests
{
    private static readonly int[] Selection = [0, 1, 2, 3];

    // Over the blocks of the selection: rule 0 is the best by far on 29 entries, too few; rules 1 and 2 tie at +0.1 R, and the first wins;
    // rule 3 is at +0.09 R. Block 4 would make rule 3 the best, but it is not of the selection.
    [Test]
    public void Best_is_the_highest_expectancy_over_the_blocks_among_the_rules_of_30_entries_the_first_on_a_tie()
    {
        BlockSums[][] rules =
        [
            [new BlockSums(29, 29m), default, default, default, default, default],
            [new BlockSums(20, 1m), new BlockSums(20, 3m), default, default, default, default],
            [new BlockSums(15, 1.5m), new BlockSums(15, 1.5m), new BlockSums(15, 1.5m), new BlockSums(15, 1.5m), default, default],
            [new BlockSums(30, 2.7m), default, default, default, new BlockSums(100, 100m), default]
        ];

        OverfittingProbability.Best(rules, Selection).ShouldBe(1);
    }

    // Scenario "Мало входов на подборе".
    [Test]
    public void Best_is_none_without_a_rule_of_30_entries()
    {
        OverfittingProbability.Best([[new BlockSums(29, 3m), default, default, default, new BlockSums(100, 1m), default]], Selection).ShouldBeNull();
    }

    // Scenario "Подгонка в большинстве разбиений": four rules of 10 entries a block and a fifth of 5, which never has 30 in a half. The 20
    // splits were counted apart from the code: the best of the first half is at the middle or below on the second in 13 of them.
    [Test]
    public void Probability_is_the_share_of_the_splits_whose_best_is_at_the_middle_or_below_on_the_other_half()
    {
        BlockSums[][] rules =
        [
            Blocks(1, 3, -3, -1, -3, 0),
            Blocks(-3, 0, 3, 3, -2, 3),
            Blocks(-2, -1, -3, 1, 1, 3),
            Blocks(0, -3, 1, 1, -2, 1),
            [.. Enumerable.Repeat(new BlockSums(5, 9m), 6)]
        ];

        Overfitting overfitting = OverfittingProbability.Of(rules)!;

        overfitting.ShouldBe(new Overfitting(20, 13));
        overfitting.Probability.ShouldBe(0.65m);
    }

    // Rule 0 has +0.1 R in every block; rule 1 has 30 entries in blocks 0 (+0.2 R) and 1 (−0.1 R) alone, so it has 30 in both halves only in
    // the 12 splits that part blocks 0 and 1. The other 8 have one rule and do not count. In each of the 12 the best of one half is the worse
    // of the two on the other: place 1 of 2, 1/3.
    [Test]
    public void Splits_with_fewer_than_two_rules_of_30_entries_in_both_halves_do_not_count()
    {
        BlockSums[][] rules = [Blocks(1, 1, 1, 1, 1, 1), [new BlockSums(30, 6m), new BlockSums(30, -3m), default, default, default, default]];

        OverfittingProbability.Of(rules).ShouldBe(new Overfitting(12, 12));
    }

    // Scenario "Мало правил с входами".
    [Test]
    public void Probability_is_none_without_a_split_that_counts()
    {
        OverfittingProbability.Of([Blocks(1, 1, 1, 1, 1, 1)]).ShouldBeNull();
    }

    private static BlockSums[] Blocks(params int[] totals)
    {
        return [.. totals.Select(total => new BlockSums(10, total))];
    }
}