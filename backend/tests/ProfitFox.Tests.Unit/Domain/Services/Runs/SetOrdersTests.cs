using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Runs.Enums;

using static ProfitFox.Tests.Unit.Domain.Services.Runs.Designs;

namespace ProfitFox.Tests.Unit.Domain.Services.Runs;

/// <summary>
///     The orders of a set over every pair of a backtest (change fix-backtest-random-control, design.md, decision 4, task
///     2.2): by week the moments and sides of the orders of the other pairs, and whether a pair has an order at a moment.
/// </summary>
public sealed class SetOrdersTests
{
    private static readonly DateTimeOffset Monday = Utc(2026, 3, 2);
    private static readonly long Week = Blocks.Of(Monday, TimeBlock.Week);

    [Test]
    public void Each_of_three_pairs_with_orders_in_a_week_sees_the_orders_of_the_other_two_and_not_its_own()
    {
        var orders = new SetOrders(
            new Dictionary<string, IReadOnlyList<PairOrder>>
            {
                ["BTCUSDT"] = [Long(Monday.AddHours(4))],
                ["ETHUSDT"] = [Short(Monday.AddHours(8)), Long(Monday.AddDays(2))],
                ["SOLUSDT"] = [Long(Monday.AddHours(12))]
            },
            TimeBlock.Week);

        Assert.That(orders.Others(Week, "BTCUSDT"), Is.EqualTo(new[] { Short(Monday.AddHours(8)), Long(Monday.AddHours(12)), Long(Monday.AddDays(2)) }));
        Assert.That(orders.Others(Week, "ETHUSDT"), Is.EqualTo(new[] { Long(Monday.AddHours(4)), Long(Monday.AddHours(12)) }));
        Assert.That(orders.Others(Week, "SOLUSDT"), Is.EqualTo(new[] { Long(Monday.AddHours(4)), Short(Monday.AddHours(8)), Long(Monday.AddDays(2)) }));
        Assert.That(orders.Others(Week, "XRPUSDT"), Has.Count.EqualTo(4), "a pair without orders sees every order of the week");
        Assert.That(orders.Weeks, Is.EqualTo(new[] { Week }));
    }

    [Test]
    public void The_same_moment_and_side_of_two_pairs_is_given_once()
    {
        var orders = new SetOrders(
            new Dictionary<string, IReadOnlyList<PairOrder>>
            {
                ["BTCUSDT"] = [Long(Monday.AddHours(12))],
                ["ETHUSDT"] = [Long(Monday.AddHours(12))],
                ["SOLUSDT"] = [Short(Monday.AddHours(12))]
            },
            TimeBlock.Week);

        Assert.That(orders.Others(Week, "XRPUSDT"), Is.EqualTo(new[] { Long(Monday.AddHours(12)), Short(Monday.AddHours(12)) }));
        Assert.That(orders.Others(Week, "BTCUSDT"), Is.EqualTo(new[] { Long(Monday.AddHours(12)), Short(Monday.AddHours(12)) }), "the long of ETHUSDT is another pair's");
    }

    [Test]
    public void A_pair_has_an_order_at_its_own_moments_whatever_the_side()
    {
        var orders = new SetOrders(new Dictionary<string, IReadOnlyList<PairOrder>> { ["ETHUSDT"] = [Short(Monday.AddHours(12))] }, TimeBlock.Week);

        Assert.That(orders.Has("ETHUSDT", Monday.AddHours(12)), Is.True);
        Assert.That(orders.Has("ETHUSDT", Monday.AddHours(16)), Is.False);
        Assert.That(orders.Has("SOLUSDT", Monday.AddHours(12)), Is.False);
    }

    [Test]
    public void Orders_go_to_the_weeks_of_their_moments()
    {
        var orders = new SetOrders(
            new Dictionary<string, IReadOnlyList<PairOrder>> { ["BTCUSDT"] = [Long(Monday.AddDays(-1)), Long(Monday.AddDays(8))] },
            TimeBlock.Week);

        Assert.That(orders.Weeks, Is.EqualTo(new[] { Week - 1, Week + 1 }));
        Assert.That(orders.Others(Week, "ETHUSDT"), Is.Empty);
        Assert.That(orders.Others(Week + 1, "ETHUSDT"), Is.EqualTo(new[] { Long(Monday.AddDays(8)) }));
    }

    private static PairOrder Long(DateTimeOffset moment)
    {
        return new PairOrder(moment, EntrySide.Long);
    }

    private static PairOrder Short(DateTimeOffset moment)
    {
        return new PairOrder(moment, EntrySide.Short);
    }
}