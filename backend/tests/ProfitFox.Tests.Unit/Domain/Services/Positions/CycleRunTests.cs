using System.Globalization;

using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Tests.Unit.Domain.Services.Trades;

namespace ProfitFox.Tests.Unit.Domain.Services.Positions;

/// <summary>
///     Change add-position-node, tasks 4.1 and 4.2: the accounting of the position of a cycle (spec strategy-position
///     «Увеличение позиции», «Уменьшение позиции») and the cycle of one entry by the rules of <c>trade-plan-outcome</c>.
///     Those rules themselves are checked on <see cref="CycleRun" /> by <see cref="TradeSimulatorTests" />,
///     <see cref="TradeExitTests" /> and <see cref="OutcomeParityTests" />, the tests of the trade simulator the cycle
///     replaced (task 5.4).
/// </summary>
public sealed class CycleRunTests
{
    private static readonly TradeCosts Free = new TradeCosts(TakerFee: 0, Slippage: 0, Funding: false, MakerFee: 0);

    [Test]
    public void A_buy_of_the_side_of_the_position_increases_it()
    {
        CycleRun cycle = Long(Paths.Start, price: 100, amount: 100);

        cycle.Increase(Paths.At(7), raw: 99, fill: 99, amount: 100, node: "buy");

        double quantity = 1 + 100.0 / 99;
        Assert.That(cycle.Quantity, Is.EqualTo(quantity).Within(1e-12));
        Assert.That(cycle.Invested, Is.EqualTo(200));
        Assert.That(cycle.AveragePrice, Is.EqualTo(200 / quantity).Within(1e-12));
        Assert.That(cycle.LastEntryPrice, Is.EqualTo(99));
        Assert.That(cycle.LastEntryTime, Is.EqualTo(Paths.At(7)));
        Assert.That(cycle.Entries, Is.EqualTo(2));
        Assert.That(cycle.MaxInvested, Is.EqualTo(200));
    }

    [Test]
    public void A_sale_larger_than_the_position_closes_it_and_opens_no_short()
    {
        CycleRun cycle = Long(Paths.Start, price: 100, amount: 200);

        double sold = cycle.Reduce(Paths.At(7), raw: 101, fill: 101, quantity: 5, CycleExit.LimitOrder, node: "sell");

        Assert.That(sold, Is.EqualTo(2));
        Assert.That(cycle.Closed, Is.True);
        Assert.That(cycle.Quantity, Is.Zero);
    }

    [Test]
    public void A_partial_exit_keeps_the_average_price()
    {
        CycleRun cycle = Long(Paths.Start, price: 100, amount: 200);

        cycle.Reduce(Paths.At(7), raw: 105, fill: 105, quantity: 1, CycleExit.LimitOrder, node: "sell");

        Assert.That(cycle.Quantity, Is.EqualTo(1));
        Assert.That(cycle.Invested, Is.EqualTo(100));
        Assert.That(cycle.AveragePrice, Is.EqualTo(100));
        Assert.That(cycle.Closed, Is.False);
        Assert.That(cycle.MaxInvested, Is.EqualTo(200), "the largest money put in stays");
    }

    [Test]
    public void The_fraction_of_a_cycle_of_two_entries_is_weighted_by_their_amounts()
    {
        MinutePath path = new Paths().Flat(100, 10).Build(withFunding: false);
        var cycle = CycleRun.Open(path, Free, EntrySide.Long, Paths.Start, raw: 100, fill: 100, amount: 100, node: "entry");
        cycle.Increase(Paths.At(1), raw: 99, fill: 99, amount: 100, node: "buy");

        cycle.Reduce(Paths.At(2), raw: 100.5, fill: 100.5, quantity: cycle.Quantity, CycleExit.LimitOrder, node: "sell");
        CycleOutcome outcome = cycle.Outcome();

        double expected = 0.5 * (100.5 / 100 - 1) + 0.5 * (100.5 / 99 - 1);
        Assert.That(outcome.Net, Is.EqualTo(expected).Within(1e-15));
        Assert.That(outcome.NetAmount, Is.EqualTo(expected * 200).Within(1e-12));
        Assert.That(outcome.NetAmount, Is.EqualTo(100.5 * (1 + 100.0 / 99) - 200).Within(1e-9), "the money of the sale less the money put in");
        Assert.That((outcome.Entries, outcome.MaxInvested, outcome.ExitReason), Is.EqualTo((2, 200.0, CycleExit.LimitOrder)));
        Assert.That(outcome.R, Is.Null, "a cycle of several entries has no result in R");
    }

    [Test]
    public void The_fee_is_taken_on_every_fill()
    {
        MinutePath path = new Paths().Flat(100, 10).Build(withFunding: false);
        var costs = new TradeCosts(TakerFee: 0.00055, Slippage: 0, Funding: false);
        var cycle = CycleRun.Open(path, costs, EntrySide.Long, Paths.Start, raw: 100, fill: 100, amount: 100, node: "entry");
        cycle.Increase(Paths.At(1), raw: 100, fill: 100, amount: 100, node: "buy");

        cycle.Reduce(Paths.At(2), raw: 100.5, fill: 100.5, quantity: 2, CycleExit.LimitOrder, node: "sell");

        Assert.That(cycle.Outcome().Fees * 200, Is.EqualTo(401 * 0.00055).Within(1e-12), "fills of 100, 100 and 201 USDT");
    }

    [Test]
    public void A_fill_of_a_limit_order_pays_the_maker_fee()
    {
        MinutePath path = new Paths().Flat(100, 10).Build(withFunding: false);
        var costs = new TradeCosts(TakerFee: 0.00055, Slippage: 0, Funding: false);
        var cycle = CycleRun.Open(path, costs, EntrySide.Long, Paths.Start, raw: 100, fill: 100, amount: 100, node: "entry");
        cycle.Increase(Paths.At(1), raw: 100, fill: 100, amount: 100, node: "buy", maker: true);

        cycle.Reduce(Paths.At(2), raw: 100.5, fill: 100.5, quantity: 2, CycleExit.LimitOrder, node: "sell", maker: true);

        Assert.That(costs.MakerFee, Is.EqualTo(0.0002), "the base rate of Bybit");
        Assert.That(cycle.Outcome().Fees * 200, Is.EqualTo(100 * 0.00055 + 301 * 0.0002).Within(1e-12), "a market entry of 100, limit fills of 100 and 201");
    }

    [Test]
    public void A_target_pays_the_taker_fee()
    {
        (MinutePath path, DateTimeOffset entry, TradePlan plan) = OutcomeFixture.Trades()
            .First(trade => CycleRun.Simulate(trade.Path, trade.Entry, trade.Plan, TradeCosts.Default, amount: 100).ExitReason == CycleExit.Target);

        CycleOutcome withMaker = CycleRun.Simulate(path, entry, plan, TradeCosts.Default, amount: 100);
        CycleOutcome withoutMaker = CycleRun.Simulate(path, entry, plan, TradeCosts.Default with { MakerFee = 0 }, amount: 100);

        Assert.That(withMaker.ExitReason, Is.EqualTo(CycleExit.Target));
        Assert.That(withMaker.Fees, Is.EqualTo(withoutMaker.Fees));
        Assert.That(withMaker.Fees, Is.EqualTo(TradeCosts.Default.TakerFee * (1 + withMaker.Fills[^1].Price / withMaker.Fills[0].Price)), "the taker fee on both sides");
    }

    [Test]
    public void Funding_after_an_increase_is_counted_on_the_whole_quantity()
    {
        MinutePath path = new Paths().Flat(100, 120).Payment(60, 0.001).Build();
        var costs = new TradeCosts(TakerFee: 0, Slippage: 0, Funding: true);
        var cycle = CycleRun.Open(path, costs, EntrySide.Long, Paths.Start, raw: 100, fill: 100, amount: 100, node: "entry");
        cycle.Increase(Paths.At(30), raw: 100, fill: 100, amount: 100, node: "buy");

        cycle.Reduce(Paths.At(90), raw: 100, fill: 100, quantity: 2, CycleExit.LimitOrder, node: "sell");

        Assert.That(cycle.Outcome().Funding * 200, Is.EqualTo(-2 * 0.001 * 100).Within(1e-12), "two coins pay the positive rate on a long");
    }

    [TestCase(100.0)]
    [TestCase(37.5)]
    public void The_amount_does_not_change_the_fractions_of_a_cycle_of_one_entry(double amount)
    {
        (MinutePath path, DateTimeOffset entry, TradePlan plan) = OutcomeFixture.Trades().First(trade => trade.Plan.Stop is not null);

        CycleOutcome outcome = CycleRun.Simulate(path, entry, plan, TradeCosts.Default, amount);

        Assert.That(Text(outcome.ToTrade()), Is.EqualTo(Text(CycleRun.Simulate(path, entry, plan, TradeCosts.Default, amount: 1).ToTrade())));
        Assert.That(outcome.NetAmount, Is.EqualTo(outcome.Net * amount));
        Assert.That((outcome.Entries, outcome.MaxInvested, outcome.Fills.Count), Is.EqualTo((1, amount, 2)));
    }

    // Every field of an outcome, the numbers in their exact bits, so that equal texts mean equal outcomes.
    internal static string Text(TradeOutcome outcome)
    {
        static string Bits(double? value)
        {
            return value is { } number ? BitConverter.DoubleToInt64Bits(number).ToString("x16", CultureInfo.InvariantCulture) : "-";
        }

        return string.Join(
            ";",
            outcome.Status,
            outcome.ExitReason,
            outcome.Ambiguous,
            outcome.EntryTime?.ToUnixTimeMilliseconds(),
            outcome.ExitTime?.ToUnixTimeMilliseconds(),
            Bits(outcome.EntryPrice),
            Bits(outcome.ExitPrice),
            Bits(outcome.Gross),
            Bits(outcome.Fees),
            Bits(outcome.Slippage),
            Bits(outcome.Funding),
            Bits(outcome.Net),
            Bits(outcome.R));
    }

    private static CycleRun Long(DateTimeOffset moment, double price, double amount)
    {
        MinutePath path = new Paths().Flat(price, 10).Build(withFunding: false);
        return CycleRun.Open(path, Free, EntrySide.Long, moment, raw: price, fill: price, amount, node: "entry");
    }
}
