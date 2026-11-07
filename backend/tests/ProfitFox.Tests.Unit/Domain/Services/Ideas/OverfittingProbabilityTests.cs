using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Requirements "Лучшее правило сетки", "Разбиения проверки" and "Вероятность подгонки" of check-idea-exit-rules-out-of-sample on reference
///     matrices: the entries and the total R of each rule of a grid on the selection, and in the bodies and the tails of the 6 blocks. The
///     answers of the splits were counted apart from the code.
/// </summary>
public class OverfittingProbabilityTests
{
    private static readonly BlockSums[] NoTails = new BlockSums[6];

    // Rule 0 is the best by far on 29 entries, too few; rules 1 and 2 tie at +0.1 R, and the first wins; rule 3 is at +0.09 R.
    [Test]
    public void Best_is_the_highest_expectancy_among_the_rules_of_30_entries_the_first_on_a_tie()
    {
        OverfittingProbability.Best([new BlockSums(29, 29m), new BlockSums(40, 4m), new BlockSums(60, 6m), new BlockSums(30, 2.7m)]).ShouldBe(1);
    }

    // Scenario "Мало входов на подборе".
    [Test]
    public void Best_is_none_without_a_rule_of_30_entries()
    {
        OverfittingProbability.Best([new BlockSums(29, 3m)]).ShouldBeNull();
    }

    // Scenario "Подгонка в большинстве разбиений": four rules of 10 entries a block and a fifth of 5, which never has 30 in a half; the best of
    // the first half is at the middle or below on the second in 13 of the 20 splits.
    [Test]
    public void Probability_is_the_share_of_the_splits_whose_best_is_at_the_middle_or_below_on_the_other_half()
    {
        RuleSums[] rules =
        [
            Rule(1, 3, -3, -1, -3, 0),
            Rule(-3, 0, 3, 3, -2, 3),
            Rule(-2, -1, -3, 1, 1, 3),
            Rule(0, -3, 1, 1, -2, 1),
            new RuleSums([.. Enumerable.Repeat(new BlockSums(5, 9m), 6)], NoTails, default, default)
        ];

        Overfitting overfitting = OverfittingProbability.Of(rules)!;

        overfitting.ShouldBe(new Overfitting(20, 13));
        overfitting.Probability.ShouldBe(0.65m);
    }

    // Rule 0 has +0.1 R in every block; rule 1 has 30 entries in blocks 0 (+0.2 R) and 1 (−0.1 R) alone, so it has 30 in both halves only in
    // the 12 splits that part blocks 0 and 1. In each of the 12 the best of one half is the worse of the two on the other.
    [Test]
    public void Splits_with_fewer_than_two_rules_of_30_entries_in_both_halves_do_not_count()
    {
        RuleSums[] rules = [Rule(1, 1, 1, 1, 1, 1), new RuleSums([new BlockSums(30, 6m), new BlockSums(30, -3m), default, default, default, default], NoTails, default, default)];

        OverfittingProbability.Of(rules).ShouldBe(new Overfitting(12, 12));
    }

    // Scenario "Мало правил с входами".
    [Test]
    public void Probability_is_none_without_a_split_that_counts()
    {
        OverfittingProbability.Of([Rule(1, 1, 1, 1, 1, 1)]).ShouldBeNull();
    }

    // Finding 1 of the review, scenario "Правила-близнецы": six twins of the truly best rule (+0.3 R in every block) and two worse ones. As
    // eight rules the best would be third of eight and an overfit in every split; as three distinct ones it is the best on the other half too.
    [Test]
    public void Twins_with_the_same_sums_in_every_block_rank_as_one_rule()
    {
        RuleSums[] rules = [.. Enumerable.Range(0, 6).Select(_ => Rule(3, 3, 3, 3, 3, 3)), Rule(1, 1, 1, 1, 1, 1), Rule(0, 0, 0, 0, 0, 0)];

        OverfittingProbability.Of(rules).ShouldBe(new Overfitting(20, 0));
    }

    // Scenario "Очистка разбиений": rule 0 has +0.2 R in the body of every block and −5 R on 10 entries in the tail of block 0; rule 1 has
    // +0.1 R. When block 1 is of the second half, the tail of block 0 leaves the first: rule 0 is the best there and on the second half, which
    // takes 6 splits out of the overfits; with the tail kept all 20 would be.
    [Test]
    public void Tail_of_a_block_of_the_first_half_before_a_block_of_the_second_does_not_count()
    {
        RuleSums[] rules =
        [
            new RuleSums([.. Enumerable.Repeat(new BlockSums(10, 2m), 6)], [new BlockSums(10, -50m), default, default, default, default, default], default, default),
            Rule(1, 1, 1, 1, 1, 1)
        ];

        OverfittingProbability.Of(rules).ShouldBe(new Overfitting(20, 14));
    }

    // The last block has no block after it: its tail counts in the first half. Rule 0 has +1 R on 30 entries in the tail of block 5 alone,
    // rule 1 +0.05 R everywhere; the best of either half is the worse on the other in all 20, and would be in 10 with the tail left out.
    [Test]
    public void Tail_of_the_last_block_counts_in_the_first_half()
    {
        RuleSums[] rules =
        [
            new RuleSums([.. Enumerable.Repeat(new BlockSums(10, 0m), 6)], [default, default, default, default, default, new BlockSums(30, 30m)], default, default),
            new RuleSums([.. Enumerable.Repeat(new BlockSums(10, 0.5m), 6)], NoTails, default, default)
        ];

        OverfittingProbability.Of(rules).ShouldBe(new Overfitting(20, 20));
    }

    private static RuleSums Rule(params int[] totals)
    {
        return new RuleSums([.. totals.Select(total => new BlockSums(10, total))], NoTails, default, default);
    }
}