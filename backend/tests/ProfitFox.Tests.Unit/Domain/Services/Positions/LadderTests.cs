using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Tests.Unit.Domain.Services.Graph;
using ProfitFox.Tests.Unit.Domain.Services.Trades;

using static ProfitFox.Tests.Unit.Domain.Services.Positions.PositionRuns;

namespace ProfitFox.Tests.Unit.Domain.Services.Positions;

/// <summary>
///     Change add-position-node, task 4.7 (design.md, decision 14): the ladder of v2 built of the nodes of the change
///     (<c>Fixtures/ladder.json</c>: rise and drop 0.01, 100 USDT an order, a deposit of 1000) on minutes written by hand,
///     without costs. The orders are checked after each fill.
/// </summary>
public sealed class LadderTests
{
    [Test]
    public void The_scenario_of_v2()
    {
        MinutePath path = new Paths()
            .Flat(100, 10)
            .Minute(99.5, 99.6, 98.9, 99.2)
            .Flat(99.5, 9)
            .Minute(99.8, 100.6, 99.7, 100.5)
            .Minute(100.4, 100.5, 100.3, 100.4)
            .Flat(100.4, 38)
            .Build(withFunding: false);
        var run = new PositionRun(PositionRuns.Strategy(Graphs.Ladder), path, Free, Paths.Start);

        // 12:00: the first computation, with no position, places the market buy of 100 USDT; it fills at the open of 12:00.
        run.Step(Hour(0), lastFunding: null);
        Assert.That(run.Standing.Select(Describe), Is.EqualTo(new[] { "entry buy market 100" }));
        run.Advance(Paths.At(1));
        Assert.That(run.Standing.Select(Describe), Is.EqualTo(new[] { "sell sell 101 1 coins", "buy buy 99 100" }), "1 coin at 100: sell at 101, buy at 99");

        // 12:10: the low 98.9 fills the buy of 100 USDT at 99. The position is q = 1 + 100 / 99 = 2.0101… coins for 200 USDT,
        // the average 200 / q = 99.4974874…; the sale moves to the average × 1.01 = 100.4924623…, the buy to 99 × 0.99.
        run.Advance(Paths.At(11));
        double quantity = 1 + 100.0 / 99;
        double sale = 200 / quantity * 1.01;
        StandingOrder sell = run.Standing.Single(order => order.Node == "sell");
        Assert.That(sell.Price, Is.EqualTo(sale).Within(1e-9));
        Assert.That(sell.Size, Is.EqualTo(quantity).Within(1e-12));
        Assert.That(run.Standing.Single(order => order.Node == "buy").Price, Is.EqualTo(98.01));

        // 12:20: the high 100.6 fills the sale of the whole quantity; the cycle closes, and the computation after it places
        // the next entry, which fills at the open of the next minute, 12:21, at 100.4.
        run.Advance(Paths.At(21));
        Assert.That(run.Standing.Select(Describe), Is.EqualTo(new[] { "entry buy market 100" }));
        PositionResult result = run.Finish(Paths.At(60));

        // The sale brings q × 1.01 × 200 / q = 202 USDT for the 200 put in: +2 USDT, 0.01 of the largest money put in.
        CycleOutcome cycle = result.Cycles[0];
        Assert.That(cycle.Fills.Select(fill => (fill.Moment, fill.Action, fill.Node)), Is.EqualTo(new[]
        {
            (Paths.At(0), FillAction.Entry, "entry"), (Paths.At(10), FillAction.Increase, "buy"), (Paths.At(20), FillAction.Exit, "sell")
        }));
        Assert.That(cycle.Fills.Select(fill => fill.Price).Take(2), Is.EqualTo(new[] { 100.0, 99.0 }));
        Assert.That((cycle.Entries, cycle.MaxInvested, cycle.ExitReason, cycle.Open), Is.EqualTo((2, 200.0, CycleExit.LimitOrder, false)));
        Assert.That(cycle.NetAmount, Is.EqualTo(2).Within(1e-9));
        Assert.That(cycle.Net, Is.EqualTo(0.01).Within(1e-12));
        Assert.That(result.Cycles[1].Fills.Single(), Is.EqualTo(new CycleFill(Paths.At(21), FillAction.Entry, FillSide.Buy, 100.4, 100 / 100.4, 100, "entry")));
        Assert.That(result.Cycles[1].Open, Is.True);
        Assert.That(result.Orders.Select(order => (order.Moment, order.Node, order.Result, order.Cycle)), Is.EqualTo(new[]
        {
            (Paths.At(0), "entry", OrderResult.Opened, (int?)0), (Paths.At(10), "buy", OrderResult.Opened, 0),
            (Paths.At(20), "sell", OrderResult.Opened, 0), (Paths.At(21), "entry", OrderResult.Opened, 1)
        }));
    }

    [Test]
    public void The_limit_fills_of_the_ladder_pay_the_maker_fee_and_its_market_entry_the_taker_fee()
    {
        MinutePath path = new Paths().Flat(100, 10).Minute(99.5, 99.6, 98.9, 99.2).Flat(99.5, 9).Minute(99.8, 100.6, 99.7, 100.5).Flat(100.4, 39).Build(withFunding: false);
        var costs = new TradeCosts(Slippage: 0, Funding: false);

        CycleOutcome cycle = Run(Graphs.Ladder, path, hours: 0, costs).Cycles[0];

        double sale = cycle.Fills[^1].Amount;
        Assert.That(cycle.Fees * cycle.MaxInvested, Is.EqualTo(100 * 0.00055 + (100 + sale) * 0.0002).Within(1e-12));
    }

    private static string Describe(StandingOrder order)
    {
        string side = order.Side == FillSide.Buy ? "buy" : "sell";
        string price = order.Price is { } limit ? limit.ToString(System.Globalization.CultureInfo.InvariantCulture) : "market";
        string size = order.Size.ToString(System.Globalization.CultureInfo.InvariantCulture) + (order.SizeInQuantity ? " coins" : "");
        return $"{order.Node} {side} {price} {size}";
    }
}
