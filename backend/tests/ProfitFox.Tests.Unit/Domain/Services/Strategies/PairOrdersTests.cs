using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies;

/// <summary>
///     Spec strategy-preview «Исход заявки», «Только разведка» and «Предел минутной истории» (change
///     add-strategy-constructor, task 5.1): the orders of a pair split by weeks, at most 8 weeks computed, one position for
///     the computed part. The history starts on Monday 2026-01-05; every minute of a week is at one price unless a test
///     writes another.
/// </summary>
public sealed class PairOrdersTests
{
    private static readonly DateTimeOffset Monday = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Monday.AddDays(7 * 60);
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);

    [Test]
    public void An_exit_by_time()
    {
        DateTimeOffset moment = Monday.AddHours(12);

        OrderOutcome outcome = Run([Order(moment, EntrySide.Short, FourHours)]).Single();

        Assert.That(outcome.Result, Is.EqualTo(OrderResult.Opened));
        Assert.That((outcome.Trade!.ExitReason, outcome.Trade.ExitTime), Is.EqualTo((ExitReason.Time, (DateTimeOffset?)moment + FourHours)));
        Assert.That(outcome.Trade.Net, Is.LessThan(0), "a flat price pays the costs");
    }

    [Test]
    public void An_exit_by_the_stop()
    {
        DateTimeOffset moment = Monday.AddHours(12);

        // The price falls 3 % an hour after the entry, before the time of four hours.
        OrderOutcome outcome = Run([Order(moment, EntrySide.Long, FourHours, stop: 0.02m)], price: time => time >= moment.AddHours(1) ? 97 : 100).Single();

        Assert.That(outcome.Result, Is.EqualTo(OrderResult.Opened));
        Assert.That(outcome.Trade!.ExitReason, Is.EqualTo(ExitReason.Stop));
        Assert.That(outcome.Trade.ExitTime, Is.LessThan(moment + FourHours));
    }

    [Test]
    public void An_order_while_the_position_is_open()
    {
        DateTimeOffset first = Monday.AddHours(12);

        IReadOnlyList<OrderOutcome> outcomes = Run([Order(first, EntrySide.Short, FourHours), Order(first.AddHours(1), EntrySide.Short, FourHours)]);

        Assert.That(outcomes.Select(outcome => outcome.Result), Is.EqualTo(new[] { OrderResult.Opened, OrderResult.Skipped }));
        Assert.That(outcomes[1].Trade, Is.Null);
    }

    [Test]
    public void An_order_that_is_not_allowed_does_not_hold_the_pair()
    {
        DateTimeOffset first = Monday.AddHours(12);
        var noStopYet = new StrategyStep(first, EntrySide.Long, FourHours, ExitDistance.Of(null), ExitDistance.None);

        IReadOnlyList<OrderOutcome> outcomes = Run([noStopYet, Order(first.AddHours(1), EntrySide.Long, FourHours)]);

        Assert.That((outcomes[0].Result, outcomes[0].Exclusion), Is.EqualTo((OrderResult.Excluded, (OrderExclusion?)OrderExclusion.Plan)));
        Assert.That(outcomes[1].Result, Is.EqualTo(OrderResult.Opened));
    }

    [Test]
    public void An_order_near_the_end_of_exploration()
    {
        DateTimeOffset end = Monday.AddDays(3);
        var orders = new PairOrders(Steps([Order(end.AddHours(-6), EntrySide.Short, FourHours), Order(end.AddHours(-2), EntrySide.Short, FourHours)]), Monday, end, TradeCosts.Default);

        Assert.That(orders.Span(orders.Weeks.Single()).To, Is.LessThanOrEqualTo(end), "nothing after the end is read");
        IReadOnlyList<OrderOutcome> outcomes = Evaluate(orders, _ => 100);

        Assert.That(outcomes.Select(outcome => (outcome.Result, outcome.Exclusion)), Is.EqualTo(new (OrderResult, OrderExclusion?)[]
        {
            (OrderResult.Opened, null), (OrderResult.Excluded, OrderExclusion.Period)
        }));
    }

    [Test]
    public void A_frequent_strategy()
    {
        StrategyStep[] steps = [.. Enumerable.Range(0, 40).Select(week => Order(Monday.AddDays(7 * week + 2), EntrySide.Short, FourHours))];
        var orders = new PairOrders(Steps(steps), Monday, End, TradeCosts.Default);

        IReadOnlyList<OrderOutcome> outcomes = Evaluate(orders, _ => 100);

        Assert.That(PairOrders.MaxWeeks, Is.EqualTo(8));
        Assert.That(orders.Weeks, Has.Count.EqualTo(8));
        Assert.That(outcomes.Take(32).Select(outcome => outcome.Result), Has.All.EqualTo(OrderResult.NotComputed));
        Assert.That(outcomes.Skip(32).Select(outcome => outcome.Result), Has.All.EqualTo(OrderResult.Opened));
        Assert.That(orders.ComputedFrom, Is.EqualTo(Monday.AddDays(7 * 32)));
    }

    [Test]
    public void A_rare_strategy()
    {
        StrategyStep[] steps = [.. Enumerable.Range(0, 5).Select(week => Order(Monday.AddDays(7 * week * 3 + 1), EntrySide.Long, FourHours))];
        var orders = new PairOrders(Steps(steps), Monday, End, TradeCosts.Default);

        IReadOnlyList<OrderOutcome> outcomes = Evaluate(orders, _ => 100);

        Assert.That(outcomes.Select(outcome => outcome.Result), Has.All.EqualTo(OrderResult.Opened));
        Assert.That(orders.Weeks, Has.Count.EqualTo(5));
        Assert.That(orders.ComputedFrom, Is.Null);
    }

    [Test]
    public void A_position_opened_in_one_week_holds_the_pair_in_the_next()
    {
        DateTimeOffset sunday = Monday.AddDays(6).AddHours(23);
        DateTimeOffset nextMonday = Monday.AddDays(7).AddHours(1);

        IReadOnlyList<OrderOutcome> outcomes = Run([Order(sunday, EntrySide.Short, FourHours), Order(nextMonday, EntrySide.Short, FourHours)]);

        Assert.That(Blocks.Of(sunday, TimeBlock.Week), Is.Not.EqualTo(Blocks.Of(nextMonday, TimeBlock.Week)), "two weeks");
        Assert.That(outcomes.Select(outcome => outcome.Result), Is.EqualTo(new[] { OrderResult.Opened, OrderResult.Skipped }));
        Assert.That(outcomes[0].Trade!.ExitTime, Is.EqualTo(sunday + FourHours), "the path of the week reaches past its end");
    }

    [Test]
    public void Weeks_go_in_the_order_of_time_and_the_outcomes_wait_for_every_week()
    {
        var orders = new PairOrders(Steps([Order(Monday.AddHours(1), EntrySide.Long, FourHours), Order(Monday.AddDays(8), EntrySide.Long, FourHours)]), Monday, End, TradeCosts.Default);

        Assert.That(orders.Outcomes, Throws.InvalidOperationException);
        orders.Evaluate(orders.Weeks[1], Path(orders.Span(orders.Weeks[1]), _ => 100));
        Assert.That(() => orders.Evaluate(orders.Weeks[0], Path(orders.Span(orders.Weeks[0]), _ => 100)), Throws.InvalidOperationException);
    }

    private static IReadOnlyList<OrderOutcome> Run(StrategyStep[] steps, Func<DateTimeOffset, double>? price = null)
    {
        return Evaluate(new PairOrders(Steps(steps), Monday, End, TradeCosts.Default), price ?? (_ => 100));
    }

    private static IReadOnlyList<OrderOutcome> Evaluate(PairOrders orders, Func<DateTimeOffset, double> price)
    {
        foreach (long week in orders.Weeks)
        {
            orders.Evaluate(week, Path(orders.Span(week), price));
        }

        return orders.Outcomes();
    }

    private static PairSteps Steps(StrategyStep[] orders)
    {
        return new PairSteps(orders, new StrategyStep(DateTimeOffset.UnixEpoch, null, null, ExitDistance.None, ExitDistance.None));
    }

    private static StrategyStep Order(DateTimeOffset moment, EntrySide side, TimeSpan after, decimal? stop = null)
    {
        return new StrategyStep(moment, side, after, stop is null ? ExitDistance.None : ExitDistance.Of(stop), ExitDistance.None);
    }

    // Every minute of the span with trades at the price of its start, funding known over the span and never paid.
    private static MinutePath Path((DateTimeOffset From, DateTimeOffset To) span, Func<DateTimeOffset, double> price)
    {
        int length = (int)(span.To - span.From).TotalMinutes;
        double[] prices = [.. Enumerable.Range(0, length).Select(minute => price(span.From.AddMinutes(minute)))];
        return new MinutePath(span.From, prices, prices, prices, prices, [.. Enumerable.Repeat(true, length)], [], span.From, span.To);
    }
}
