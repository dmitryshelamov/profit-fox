using ProfitFox.Domain.Services.IdeaTrades;
using ProfitFox.Domain.ValueObjects.IdeaTrades;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.IdeaTrades;

/// <summary>Requirement "Ликвидация и ADL": one execution of the exchange is split between the trades of its side by the quantity they hold.</summary>
public class ForcedFillSharesTests
{
    private static readonly Guid First = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Second = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Third = Guid.Parse("33333333-3333-4333-8333-333333333333");

    // Scenario "Две сделки на символе": 300 and 100 held, 400 liquidated at 2.5 with the fee 0.4.
    [Test]
    public void Execution_of_all_that_is_held_gives_each_trade_its_quantity_at_the_same_price_and_its_part_of_the_fee()
    {
        IReadOnlyList<ForcedFillShare> shares = ForcedFillShares.Split(400m, 2.5m, 0.4m, 1m, [new ForcedFillHolder(First, 300m), new ForcedFillHolder(Second, 100m)]);

        shares.ShouldBe([new ForcedFillShare(First, new LegTotals(300m, 750m, 0.3m)), new ForcedFillShare(Second, new LegTotals(100m, 250m, 0.1m))]);
    }

    // The execution took more than the trades hold: the rest was a position of nobody here, each trade gives up all it holds.
    [Test]
    public void Execution_above_what_is_held_gives_each_trade_its_whole_quantity()
    {
        IReadOnlyList<ForcedFillShare> shares = ForcedFillShares.Split(500m, 2.5m, 0.4m, 1m, [new ForcedFillHolder(First, 300m), new ForcedFillHolder(Second, 100m)]);

        shares.ShouldBe([new ForcedFillShare(First, new LegTotals(300m, 750m, 0.24m)), new ForcedFillShare(Second, new LegTotals(100m, 250m, 0.08m))]);
    }

    // A part of what is held: each share is rounded down to the step, and what the rounding leaves goes to the last trade by id.
    [Test]
    public void Execution_of_a_part_splits_by_the_quantity_held_and_the_last_trade_takes_what_the_rounding_leaves()
    {
        IReadOnlyList<ForcedFillShare> shares = ForcedFillShares.Split(100m, 2m, 0.3m, 1m, [new ForcedFillHolder(First, 200m), new ForcedFillHolder(Second, 100m)]);

        shares.ShouldBe([new ForcedFillShare(First, new LegTotals(66m, 132m, 0.198m)), new ForcedFillShare(Second, new LegTotals(34m, 68m, 0.102m))]);
    }

    // The last trade takes what the rounding leaves only up to what it holds.
    [Test]
    public void Share_of_the_last_trade_is_no_more_than_it_holds()
    {
        IReadOnlyList<ForcedFillShare> shares = ForcedFillShares.Split(
            2m,
            3m,
            0.2m,
            1m,
            [new ForcedFillHolder(First, 1m), new ForcedFillHolder(Second, 1m), new ForcedFillHolder(Third, 1m)]);

        shares.Select(share => share.Totals.Quantity).ShouldBe([0m, 0m, 1m]);
    }

    [Test]
    public void Execution_with_no_trade_that_holds_has_no_shares()
    {
        ForcedFillShares.Split(400m, 2.5m, 0.4m, 1m, []).ShouldBeEmpty();
    }
}