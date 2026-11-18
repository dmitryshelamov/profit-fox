using ProfitFox.Engine.Simulation.Market;
using ProfitFox.Engine.Strategy.Execution;

namespace ProfitFox.Tests.Unit.Engine.Simulation.Market;

[TestFixture]
public class MinutePathTests
{
    [Test]
    public void A_candle_outside_the_coverage_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Paths.Path(minutes: 5, [Paths.Flat(5, 100m)]));
    }

    [Test]
    public void Candles_out_of_order_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Paths.Path(minutes: 5, [Paths.Flat(1, 100m), Paths.Flat(0, 100m)]));
    }

    [Test]
    public void A_candle_not_on_a_whole_minute_is_rejected()
    {
        var candle = new MinuteCandle(Paths.Ten.AddSeconds(30), 1m, 1m, 1m, 1m, 1m);

        Assert.Throws<ArgumentException>(() => Paths.Path(minutes: 5, [candle]));
    }

    [Test]
    public void Overlapping_spans_of_coverage_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Paths.Path(
            minutes: 5,
            [],
            coverage: [new TimeRange(Paths.Minute(0), Paths.Minute(3)), new TimeRange(Paths.Minute(2), Paths.Minute(5))]));
    }

    [Test]
    public void A_payment_of_another_instrument_is_rejected()
    {
        var payment = new FundingPayment("ETHUSDT", Paths.Minute(1), 0.0001m);

        Assert.Throws<ArgumentException>(() => Paths.Path(minutes: 5, [], [payment]));
    }

    [Test]
    public void A_candle_of_volume_zero_is_a_minute_without_trades()
    {
        MinutePath path = Paths.Path(minutes: 5, [Paths.Flat(0, 100m, volume: 0m), Paths.Flat(1, 101m, volume: 3m)]);

        Assert.That(path.FirstTradeAtOrAfter(Paths.Ten)?.OpenTime, Is.EqualTo(Paths.Minute(1)));
    }

    [Test]
    public void The_first_trade_after_an_uncovered_minute_is_not_reached()
    {
        MinutePath path = Paths.Path(
            minutes: 3,
            [Paths.Flat(2, 100m)],
            coverage: [new TimeRange(Paths.Minute(0), Paths.Minute(1)), new TimeRange(Paths.Minute(2), Paths.Minute(3))]);

        Assert.Multiple(() =>
        {
            Assert.That(path.FirstTradeAtOrAfter(Paths.Minute(0)), Is.Null);
            Assert.That(path.FirstTradeAtOrAfter(Paths.Minute(2))?.Open, Is.EqualTo(100m));
            Assert.That(path.IsCovered(Paths.Minute(0), Paths.Minute(1)), Is.True);
            Assert.That(path.IsCovered(Paths.Minute(0), Paths.Minute(2)), Is.False);
        });
    }

    [Test]
    public void The_close_before_a_moment_is_carried_over_minutes_without_trades()
    {
        MinutePath path = Paths.Path(minutes: 4, [Paths.Flat(0, 100m), Paths.Flat(1, 105m, volume: 0m), Paths.Flat(3, 107m)]);

        Assert.Multiple(() =>
        {
            Assert.That(path.LastTradedCloseAt(Paths.Minute(0)), Is.Null);
            Assert.That(path.LastTradedCloseAt(Paths.Minute(3)), Is.EqualTo(100m));
            Assert.That(path.LastTradedCloseAt(Paths.Minute(4)), Is.EqualTo(107m));
        });
    }
}