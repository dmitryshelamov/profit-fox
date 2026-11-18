using ProfitFox.Engine.Simulation.Market;
using ProfitFox.Engine.Simulation.Orders;

namespace ProfitFox.Tests.Unit.Engine.Simulation.Orders;

[TestFixture]
public class ExchangeSimulatorTests
{
    private static readonly Costs Costs = new Costs(takerFee: 0.00055m, slippage: 0.0005m);

    [Test]
    public void A_buy_for_an_amount_fills_at_the_open_of_its_minute_with_slippage()
    {
        var simulator = new ExchangeSimulator(Paths.Path(minutes: 2, [Paths.Candle(0, 100m, 101m, 99m, 100.5m)]), Costs);

        Fill? fill = simulator.Execute(MarketOrder.ForAmount(Paths.Ten, OrderSide.Buy, amount: 1m));

        Assert.That(fill, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(fill!.Time, Is.EqualTo(Paths.Ten));
            Assert.That(fill.Price, Is.EqualTo(100.05m));
            Assert.That(fill.MarketPrice, Is.EqualTo(100m));
            Assert.That(fill.Quantity, Is.EqualTo(1m / 100.05m));
        });
    }

    [Test]
    public void A_sale_after_a_minute_without_trades_fills_at_the_next_minute_with_trades()
    {
        var simulator = new ExchangeSimulator(Paths.Path(minutes: 2, [Paths.Flat(1, 99m)]), Costs);

        Fill? fill = simulator.Execute(MarketOrder.ForQuantity(Paths.Ten, OrderSide.Sell, quantity: 1m));

        Assert.Multiple(() =>
        {
            Assert.That(fill?.Time, Is.EqualTo(Paths.Minute(1)));
            Assert.That(fill?.Price, Is.EqualTo(98.9505m));
        });
    }

    [Test]
    public void An_order_does_not_fill_on_the_minutes_before_it()
    {
        var simulator = new ExchangeSimulator(Paths.Path(minutes: 3, [Paths.Flat(0, 90m), Paths.Flat(2, 100m)]), Costs);

        Fill? fill = simulator.Execute(MarketOrder.ForQuantity(Paths.Minute(1), OrderSide.Buy, quantity: 1m));

        Assert.That(fill?.MarketPrice, Is.EqualTo(100m));
    }

    [Test]
    public void An_order_with_an_uncovered_minute_before_the_first_trade_does_not_fill()
    {
        MinutePath path = Paths.Path(
            minutes: 3,
            [Paths.Flat(2, 100m)],
            coverage: [new TimeRange(Paths.Minute(0), Paths.Minute(1)), new TimeRange(Paths.Minute(2), Paths.Minute(3))]);
        var simulator = new ExchangeSimulator(path, Costs);

        Assert.That(simulator.Execute(MarketOrder.ForQuantity(Paths.Ten, OrderSide.Buy, quantity: 1m)), Is.Null);
    }

    [Test]
    public void An_order_after_the_end_of_the_path_does_not_fill()
    {
        var simulator = new ExchangeSimulator(Paths.Path(minutes: 1, [Paths.Flat(0, 100m)]), Costs);

        Assert.That(simulator.Execute(MarketOrder.ForQuantity(Paths.Minute(1), OrderSide.Sell, quantity: 1m)), Is.Null);
    }

    [Test]
    public void An_order_not_at_a_whole_minute_is_rejected()
    {
        var simulator = new ExchangeSimulator(Paths.Path(minutes: 1, [Paths.Flat(0, 100m)]), Costs);

        Assert.Throws<ArgumentException>(() => simulator.Execute(MarketOrder.ForQuantity(Paths.Ten.AddSeconds(1), OrderSide.Buy, quantity: 1m)));
    }

    [Test]
    public void The_taker_fee_is_charged_on_the_value_of_the_fill()
    {
        var simulator = new ExchangeSimulator(Paths.Path(minutes: 1, [Paths.Flat(0, 100m)]), new Costs(takerFee: 0.00055m, slippage: 0m));

        Fill? fill = simulator.Execute(MarketOrder.ForQuantity(Paths.Ten, OrderSide.Buy, quantity: 0.01m));

        Assert.That(fill?.Fee, Is.EqualTo(0.00055m));
    }

    [Test]
    public void Funding_counts_the_payments_after_the_entry_up_to_the_exit_at_the_close_before_each()
    {
        MinutePath path = Paths.Path(
            minutes: 5,
            [Paths.Flat(0, 101m), Paths.Flat(1, 100m), Paths.Flat(2, 99m), Paths.Flat(3, 99m), Paths.Flat(4, 99m)],
            [Paths.Payment(0, 0.01m), Paths.Payment(1, 0.0001m), Paths.Payment(3, 0.0002m), Paths.Payment(4, 0.01m)]);
        var simulator = new ExchangeSimulator(path, Costs);

        decimal? shortReceived = simulator.Funding(PositionSide.Short, quantity: 1m, Paths.Minute(0), Paths.Minute(3));
        decimal? longReceived = simulator.Funding(PositionSide.Long, quantity: 1m, Paths.Minute(0), Paths.Minute(3));

        Assert.Multiple(() =>
        {
            Assert.That(shortReceived, Is.EqualTo(0.0001m * 101m + 0.0002m * 99m));
            Assert.That(longReceived, Is.EqualTo(-(0.0001m * 101m + 0.0002m * 99m)));
        });
    }

    [Test]
    public void A_negative_rate_is_paid_by_the_short()
    {
        MinutePath path = Paths.Path(minutes: 2, [Paths.Flat(0, 100m), Paths.Flat(1, 100m)], [Paths.Payment(1, -0.001m)]);
        var simulator = new ExchangeSimulator(path, Costs);

        Assert.That(simulator.Funding(PositionSide.Short, quantity: 2m, Paths.Minute(0), Paths.Minute(1)), Is.EqualTo(-0.2m));
    }

    [Test]
    public void Funding_is_unknown_when_the_payments_are_not_known_up_to_the_exit()
    {
        MinutePath path = Paths.Path(minutes: 4, [Paths.Flat(0, 100m)], fundingCoverage: new TimeRange(Paths.Minute(0), Paths.Minute(2)));
        var simulator = new ExchangeSimulator(path, Costs);

        Assert.Multiple(() =>
        {
            Assert.That(simulator.Funding(PositionSide.Short, quantity: 1m, Paths.Minute(0), Paths.Minute(3)), Is.Null);
            Assert.That(simulator.Funding(PositionSide.Short, quantity: 1m, Paths.Minute(0), Paths.Minute(1)), Is.EqualTo(0m));
        });
    }
}