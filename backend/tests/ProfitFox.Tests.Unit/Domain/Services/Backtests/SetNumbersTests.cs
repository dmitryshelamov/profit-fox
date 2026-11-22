using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Hypotheses.Enums;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>
///     Change add-strategy-backtest, task 4.2: spec strategy-backtest «Результат набора», «Результат по парам» and «Разница
///     со случайным входом» — the numbers of a set are the sum of those of its pairs.
/// </summary>
public sealed class SetNumbersTests
{
    private static readonly DateTimeOffset Monday = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly long Week = Blocks.Of(Monday, TimeBlock.Week);

    [Test]
    public void Three_trades()
    {
        SetNumbers numbers = PairNumbers.Of([Trade(0.02), Trade(-0.01), Trade(0.005)], skipped: 0, pastEnd: 0);

        Assert.That(numbers.Trades, Is.EqualTo(3));
        Assert.That(numbers.Mean, Is.EqualTo(0.005).Within(1e-12));
        Assert.That(numbers.Amount, Is.EqualTo(1.5).Within(1e-12));
        Assert.That(numbers.WinShare, Is.EqualTo(2.0 / 3));
        Assert.That(numbers.PairsWithTrades, Is.EqualTo(1));
    }

    [Test]
    public void A_set_without_trades()
    {
        SetNumbers numbers = PairNumbers.Of([Left(TradeStatus.Coverage)], skipped: 1, pastEnd: 1);

        Assert.That((numbers.Trades, numbers.Amount, numbers.Mean, numbers.WinShare), Is.EqualTo((0, 0.0, (double?)null, (double?)null)));
        Assert.That(numbers.Excluded, Is.EqualTo(new Exclusions(Coverage: 1, Period: 1)));
        Assert.That((numbers.Orders, numbers.Skipped, numbers.PairsWithTrades), Is.EqualTo((3, 1, 0)));
        Assert.That(SetNumbers.Zero.Mean, Is.Null);
        Assert.That(SetNumbers.Zero.Difference(), Is.Null);
    }

    [Test]
    public void An_open_cycle_is_counted_apart()
    {
        var result = new PositionResult([Cycle(0.01, 1.0), Cycle(-0.02, -2.0), Cycle(-0.0817, -8.17, open: true)], [Filled(), Filled(), Filled()]);

        SetNumbers numbers = PairNumbers.Of(result);

        Assert.That((numbers.Trades, numbers.OpenCycles), Is.EqualTo((2, 1)));
        Assert.That(numbers.OpenAmount, Is.EqualTo(-8.17));
        Assert.That(numbers.Amount, Is.EqualTo(-1.0).Within(1e-12), "the estimate of the open cycle is not in the sum");
        Assert.That(numbers.Mean, Is.EqualTo(-0.005).Within(1e-12));
        Assert.That(numbers.Difference(), Is.Null, "a strategy for a bot has no random entries");
    }

    [Test]
    public void The_orders_of_a_bot_are_counted_by_their_outcome()
    {
        OrderOutcome period = Filled() with { Result = OrderResult.Excluded, Exclusion = OrderExclusion.Period };
        OrderOutcome skipped = Filled() with { Result = OrderResult.Skipped };

        SetNumbers numbers = PairNumbers.Of(new PositionResult([], [period, period, skipped]));

        Assert.That((numbers.Orders, numbers.Excluded.Period, numbers.Skipped, numbers.Trades), Is.EqualTo((3, 2, 1, 0)));
    }

    [Test]
    public void Two_pairs_add_up_to_the_set_and_go_by_their_sum()
    {
        SetNumbers btc = PairNumbers.Of([Trade(0.006), Trade(0.006)], skipped: 0, pastEnd: 0);
        SetNumbers eth = PairNumbers.Of([Trade(-0.004)], skipped: 0, pastEnd: 0);
        SetNumbers none = PairNumbers.Of([], skipped: 0, pastEnd: 0);

        IReadOnlyList<(string Pair, SetNumbers Numbers)> pairs = PairNumbers.Ordered([("ETHUSDT", eth), ("SOLUSDT", none), ("BTCUSDT", btc)]);
        SetNumbers set = pairs.Aggregate(SetNumbers.Zero, (sum, pair) => sum + pair.Numbers);

        Assert.That(pairs.Select(pair => pair.Pair), Is.EqualTo(new[] { "BTCUSDT", "ETHUSDT" }), "a pair without orders is not in the result");
        Assert.That(btc.Amount, Is.EqualTo(1.2).Within(1e-12));
        Assert.That(eth.Amount, Is.EqualTo(-0.4).Within(1e-12));
        Assert.That((set.Trades, set.PairsWithTrades), Is.EqualTo((3, 2)));
        Assert.That(set.Amount, Is.EqualTo(0.8).Within(1e-12));
    }

    [Test]
    public void Two_orders_in_one_week_average_into_its_mean()
    {
        SetNumbers btc = PairNumbers.Of([Counted(Week, 0.004)], skipped: 0, pastEnd: 0);
        SetNumbers eth = PairNumbers.Of([Counted(Week, -0.002)], skipped: 0, pastEnd: 0);

        SetNumbers set = btc + eth;

        Assert.That(set.WeekMeans().Single().Mean, Is.EqualTo(0.001).Within(1e-15));
        Assert.That(set.WeekMeans().Single().Orders, Is.EqualTo(2));
    }

    [Test]
    public void An_order_with_one_random_entry_does_not_count()
    {
        EventRow one = Counted(Week, 0.004) with { RandomValid = 1 };

        Assert.That(PairNumbers.Of([one], skipped: 0, pastEnd: 0).Difference(), Is.Null);
        Assert.That(PairNumbers.Of([one], skipped: 0, pastEnd: 0).Trades, Is.EqualTo(1), "its trade is a trade all the same");
    }

    [Test]
    public void One_week_gives_a_difference_without_t()
    {
        RandomDifference difference = PairNumbers.Of([Counted(Week, 0.004), Counted(Week, 0.002)], skipped: 0, pastEnd: 0).Difference()!;

        Assert.That((difference.Weeks, difference.T), Is.EqualTo((1, (double?)null)));
        Assert.That(difference.Mean, Is.EqualTo(0.003).Within(1e-15));
    }

    [Test]
    public void The_t_is_the_mean_over_its_error_by_weeks()
    {
        RandomDifference difference = PairNumbers.Of([Counted(Week, 0.001), Counted(Week + 1, 0.003), Counted(Week + 2, 0.005)], skipped: 0, pastEnd: 0).Difference()!;

        // The mean 0.003, the spread 0.002, three weeks: t = 0.003 / (0.002 / √3).
        Assert.That(difference.Weeks, Is.EqualTo(3));
        Assert.That(difference.Mean, Is.EqualTo(0.003).Within(1e-15));
        Assert.That(difference.T, Is.EqualTo(0.003 / (0.002 / Math.Sqrt(3))).Within(1e-9));
    }

    // A closed trade of 100 USDT with the result net.
    private static EventRow Trade(double net)
    {
        return Row(Monday.AddHours(4), TradeStatus.Ok, net, randoms: 0) with { NetAmount = net * 100 };
    }

    // A trade with two valid random entries whose difference is the given one.
    private static EventRow Counted(long week, double difference)
    {
        EventRow row = Row(Blocks.Start(week, TimeBlock.Week).AddHours(4), TradeStatus.Ok, 0.001 + difference, randoms: 2) with { NetAmount = 0.1 };
        return row;
    }

    private static EventRow Left(TradeStatus status)
    {
        return new EventRow("BTCUSDT", Monday, Week, status, TradeOutcome.Excluded(status), Event: null, [], 0, RandomMean: null, PseudoDifference: null);
    }

    private static EventRow Row(DateTimeOffset moment, TradeStatus status, double net, int randoms)
    {
        var outcome = new TradeOutcome(status, ExitReason.Time, Ambiguous: false, moment, moment.AddHours(4), 100, 100, net, 0, 0, 0, net, R: null);
        var fields = TradeFields.Of(outcome, Measure.Net);
        TradeFields mean = fields with { Y = 0.001 };
        return new EventRow("BTCUSDT", moment, Blocks.Of(moment, TimeBlock.Week), status, outcome, fields, [], randoms, randoms > 0 ? mean : null, PseudoDifference: null);
    }

    private static CycleOutcome Cycle(double net, double amount, bool open = false)
    {
        return new CycleOutcome(TradeStatus.Ok, EntrySide.Long, [], 1, 100, open ? CycleExit.None : CycleExit.LimitOrder, Ambiguous: false, net, 0, 0, 0, net, amount, R: null, open);
    }

    private static OrderOutcome Filled()
    {
        return new OrderOutcome(Monday, "entry", OrderSide.Buy, OrderResult.Filled, Exclusion: null, Price: 100, Amount: 100, Cycle: 0);
    }
}
