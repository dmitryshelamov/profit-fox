using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Graph;
using ProfitFox.Tests.Unit.Domain.Services.Trades;

using static ProfitFox.Tests.Unit.Domain.Services.Positions.PositionRuns;

namespace ProfitFox.Tests.Unit.Domain.Services.Positions;

/// <summary>
///     Change add-position-node, tasks 4.3–4.6: the orders of the nodes and how they are kept, replaced and taken off (spec
///     strategy-execution «Узел входа», «Рыночная заявка», «Лимитная заявка», «Замена и снятие лимитной заявки», «Закрытие
///     позиции по условию»), their fills minute by minute and the graph computed again after each (spec strategy-position),
///     the stop, the target and the time of a cycle of several entries, its result, an open cycle and a hole in the
///     history. Candles close every hour from 12:00 (<see cref="Paths.Start" />).
/// </summary>
public sealed class PositionRunTests
{
    // Buys 100 USDT by the market while there is no position.
    private const string EntryNodes =
        """
        { "id": "closed", "kind": "not" },
        { "id": "entry", "kind": "market-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "size": 100 } }
        """;

    private const string EntryWires =
        """
        { "from": "position.open", "to": "closed.value" }, { "from": "closed.result", "to": "entry.when" }, { "from": "entry.order", "to": "position.orders" }
        """;

    // A limit buy of 100 USDT 1 % below the last entry while the position is open.
    private const string AverageNodes =
        """
        { "id": "drop", "kind": "multiply", "inputs": { "b": 0.99 } },
        { "id": "buy", "kind": "limit-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "size": 100 } }
        """;

    private const string AverageWires =
        """
        { "from": "position.lastEntryPrice", "to": "drop.a" }, { "from": "drop.result", "to": "buy.price" },
        { "from": "position.open", "to": "buy.when" }, { "from": "buy.order", "to": "position.orders" }
        """;

    [Test]
    public void The_first_entry_without_a_signal_comes_at_the_first_computation()
    {
        PositionResult result = Run(WithPosition(EntryNodes, EntryWires), new Paths().Flat(100, 60).Build(withFunding: false), hours: 0);

        CycleOutcome cycle = result.Cycles.Single();
        Assert.That(cycle.Side, Is.EqualTo(EntrySide.Long), "a market buy with the position closed opens a long");
        Assert.That(cycle.Fills.Single().Moment, Is.EqualTo(Paths.Start));
        Assert.That(cycle.Open, Is.True);
        Assert.That(result.Orders.Single(), Is.EqualTo(new OrderOutcome(Paths.Start, "entry", OrderSide.Buy, OrderResult.Filled, null, 100, 100, 0)));
    }

    [Test]
    public void The_entry_fills_at_the_open_of_its_minute_worse_by_the_slippage()
    {
        var costs = new TradeCosts(TakerFee: 0, Slippage: 0.0005, Funding: false);

        PositionResult result = Run(WithPosition(EntryNodes, EntryWires), new Paths().Minute(100, 101, 99, 100.5).Flat(100.5, 59).Build(withFunding: false), hours: 0, costs);

        Assert.That(result.Cycles.Single().Fills.Single().Price, Is.EqualTo(100 * (1 + 0.0005)));
    }

    [Test]
    public void A_condition_that_does_not_depend_on_the_position_gives_an_order_after_each_fill()
    {
        string json = WithPosition(
            """{ "id": "always", "kind": "market-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "when": true, "size": 100 } }""",
            Into("always"));

        PositionResult result = Run(json, new Paths().Quiet(3).Flat(100, 57).Build(withFunding: false), hours: 0);

        CycleOutcome cycle = result.Cycles.Single();
        Assert.That(cycle.Fills.Select(fill => fill.Moment).Take(3), Is.EqualTo(new[] { Paths.At(3), Paths.At(4), Paths.At(5) }), "one order at a time: the first waits for a minute with trades");
        Assert.That(cycle.Entries, Is.EqualTo(57));
    }

    [Test]
    public void A_market_order_without_a_size_gives_no_order()
    {
        string json = WithPosition(
            """
            { "id": "c", "kind": "candle" }, { "id": "prev", "kind": "previous" },
            { "id": "always", "kind": "market-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "when": true } }
            """,
            $$"""{ "from": "c.close", "to": "prev.value" }, { "from": "prev.result", "to": "always.size" }, {{Into("always")}}""");

        PositionResult result = Run(json, new Paths().Flat(100, 120).Build(withFunding: false), hours: 1);

        Assert.That(result.Orders[0].Moment, Is.EqualTo(Paths.At(60)), "the size is missing at the first candle");
    }

    [Test]
    public void The_entry_of_a_signal_places_a_market_order_at_the_close_of_the_candle_of_its_event()
    {
        PositionResult result = Run(Graphs.Raised(SignalOnTheSecondCandle("short", "4h")), new Paths().Flat(100, 7 * 60).Build(withFunding: false), hours: 6, close: Rising);

        CycleOutcome cycle = result.Cycles.Single();
        Assert.That(cycle.Side, Is.EqualTo(EntrySide.Short));
        Assert.That(cycle.Fills[0].Moment, Is.EqualTo(Paths.At(60)), "the event is at the close of 13:00");
        Assert.That(cycle.Fills[0].Amount, Is.EqualTo(100));
        Assert.That((cycle.Fills[^1].Moment, cycle.ExitReason), Is.EqualTo((Paths.At(5 * 60), CycleExit.Time)));
    }

    [Test]
    public void An_entry_without_a_size_is_refused_with_the_reason_plan()
    {
        // The size is the average price of the position times 1: missing while the position is closed.
        const string json =
            """
            {
              "formatVersion": 1,
              "nodes": [
                { "id": "c", "kind": "candle" },
                { "id": "above", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 100 } },
                { "id": "first", "kind": "first-time" },
                { "id": "position", "kind": "position" },
                { "id": "none", "kind": "multiply", "inputs": { "b": 1 } },
                { "id": "enter", "kind": "enter", "settings": { "side": "long" } },
                { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } }
              ],
              "wires": [
                { "from": "c.close", "to": "above.a" }, { "from": "above.result", "to": "first.value" }, { "from": "first.event", "to": "enter.when" },
                { "from": "position.averagePrice", "to": "none.a" }, { "from": "none.result", "to": "enter.size" },
                { "from": "enter.order", "to": "position.orders" },
                { "from": "position.position", "to": "close-after.position" }, { "from": "close-after.order", "to": "position.orders" }
              ]
            }
            """;

        PositionResult result = Run(json, new Paths().Flat(100, 7 * 60).Build(withFunding: false), hours: 6, close: Rising);

        Assert.That(result.Orders.Single(), Is.EqualTo(new OrderOutcome(Paths.At(60), "enter", OrderSide.Long, OrderResult.Excluded, OrderExclusion.Plan, null, null, null)));
        Assert.That(result.Cycles, Is.Empty);
    }

    [Test]
    public void A_market_order_waits_for_a_minute_with_trades_and_gives_no_second_order_meanwhile()
    {
        string json = WithPosition(
            """{ "id": "always", "kind": "market-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "when": true, "size": 100 } }""",
            Into("always"));

        PositionResult result = Run(json, new Paths().Quiet(60).Flat(100, 60).Build(withFunding: false), hours: 1);

        Assert.That(result.Orders.Take(2).Select(order => order.Moment), Is.EqualTo(new[] { Paths.At(60), Paths.At(61) }), "the order of 12:00 fills at 13:00, the next one at 13:01");
    }

    [Test]
    public void A_limit_order_is_kept_while_its_price_and_size_stay()
    {
        var run = new PositionRun(Strategy(LimitBuy(""" "inputs": { "when": true, "price": 99, "size": 100 } """, "")), new Paths().Flat(100, 120).Build(withFunding: false), Free, Paths.Start);

        run.Step(Hour(0), lastFunding: null);
        StandingOrder first = run.Standing.Single();
        run.Step(Hour(1), lastFunding: null);

        Assert.That(run.Standing.Single(), Is.SameAs(first));
        Assert.That((first.Side, first.Price, first.Size), Is.EqualTo((FillSide.Buy, (double?)99, 100.0)));
    }

    [Test]
    public void A_limit_order_is_replaced_when_its_price_changes()
    {
        string json = LimitBuy(""" "inputs": { "when": true, "size": 100 } """, """{ "from": "below.result", "to": "buy.price" }""", """{ "id": "c", "kind": "candle" }, { "id": "below", "kind": "multiply", "inputs": { "b": 0.99 } },""", """{ "from": "c.close", "to": "below.a" },""");
        var run = new PositionRun(Strategy(json), new Paths().Flat(100, 120).Build(withFunding: false), Free, Paths.Start);

        run.Step(Hour(0, 100), lastFunding: null);
        StandingOrder first = run.Standing.Single();
        run.Step(Hour(1, 99), lastFunding: null);

        Assert.That(first.Price, Is.EqualTo(99));
        Assert.That(run.Standing.Single(), Is.Not.SameAs(first));
        Assert.That(run.Standing.Single().Price, Is.EqualTo(98.01));
    }

    [Test]
    public void A_limit_order_is_taken_off_when_its_condition_becomes_false()
    {
        string json = LimitBuy(""" "inputs": { "price": 90, "size": 100 } """, """{ "from": "high.result", "to": "buy.when" }""", """{ "id": "c", "kind": "candle" }, { "id": "high", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 99.5 } },""", """{ "from": "c.close", "to": "high.a" },""");
        var run = new PositionRun(Strategy(json), new Paths().Flat(100, 120).Build(withFunding: false), Free, Paths.Start);

        run.Step(Hour(0, 100), lastFunding: null);
        Assert.That(run.Standing, Has.Count.EqualTo(1));
        run.Step(Hour(1, 99), lastFunding: null);

        Assert.That(run.Standing, Is.Empty);
    }

    [Test]
    public void A_limit_order_without_a_price_yet_is_not_placed()
    {
        string json = LimitBuy(""" "inputs": { "when": true, "size": 100 } """, """{ "from": "prev.result", "to": "buy.price" }""", """{ "id": "c", "kind": "candle" }, { "id": "prev", "kind": "previous" },""", """{ "from": "c.close", "to": "prev.value" },""");
        var run = new PositionRun(Strategy(json), new Paths().Flat(100, 120).Build(withFunding: false), Free, Paths.Start);

        run.Step(Hour(0, 100), lastFunding: null);

        Assert.That(run.Standing, Is.Empty);
    }

    [Test]
    public void A_sale_in_coins_carries_the_whole_quantity_of_the_position()
    {
        var run = new PositionRun(Strategy(Graphs.Ladder), new Paths().Flat(100, 60).Build(withFunding: false), Free, Paths.Start);

        run.Step(Hour(0), lastFunding: null);
        run.Advance(Paths.At(1));

        StandingOrder sell = run.Standing.Single(order => order.Node == "sell");
        Assert.That((sell.Side, sell.Price, sell.Size, sell.SizeInQuantity), Is.EqualTo((FillSide.Sell, (double?)101, 1.0, true)));
        StandingOrder buy = run.Standing.Single(order => order.Node == "buy");
        Assert.That((buy.Side, buy.Price, buy.Size, buy.SizeInQuantity), Is.EqualTo((FillSide.Buy, (double?)99, 100.0, false)));
    }

    [Test]
    public void Close_position_sells_the_whole_position_by_the_market()
    {
        string json = WithPosition(
            $$"""{{EntryNodes}}, { "id": "out", "kind": "close-position" }""",
            $$"""{{EntryWires}}, { "from": "position.open", "to": "out.when" }, {{Into("out")}}""");

        PositionResult result = Run(json, new Paths().Flat(100, 60).Build(withFunding: false), hours: 0);

        CycleOutcome first = result.Cycles[0];
        Assert.That(first.Fills.Select(fill => (fill.Moment, fill.Action, fill.Side)), Is.EqualTo(new[]
        {
            (Paths.At(0), FillAction.Entry, FillSide.Buy), (Paths.At(1), FillAction.Exit, FillSide.Sell)
        }));
        Assert.That(first.ExitReason, Is.EqualTo(CycleExit.ClosePosition));
        Assert.That(result.Cycles[1].Fills[0].Moment, Is.EqualTo(Paths.At(2)), "the next entry comes with the computation after the close");
    }

    [Test]
    public void Close_position_gives_no_order_while_the_position_is_closed()
    {
        string json = WithPosition(
            """
            { "id": "never", "kind": "market-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "when": false, "size": 100 } },
            { "id": "out", "kind": "close-position", "inputs": { "when": true } }
            """,
            $$"""{{Into("never")}}, {{Into("out")}}""");
        var run = new PositionRun(Strategy(json), new Paths().Flat(100, 60).Build(withFunding: false), Free, Paths.Start);

        run.Step(Hour(0), lastFunding: null);

        Assert.That(run.Standing, Is.Empty);
    }

    [Test]
    public void A_limit_buy_fills_at_its_price_in_the_minute_its_low_reaches_it()
    {
        PositionResult result = Run(
            LimitBuy(""" "inputs": { "when": true, "price": 99, "size": 100 } """, ""),
            new Paths().Flat(100, 7).Minute(99.5, 99.6, 98.9, 99.2).Flat(99.2, 52).Build(withFunding: false),
            hours: 0);

        CycleFill fill = result.Cycles[0].Fills[0];
        Assert.That((fill.Moment, fill.Price, fill.Action), Is.EqualTo((Paths.At(7), 99.0, FillAction.Entry)));
    }

    [Test]
    public void A_limit_buy_fills_at_its_price_on_a_gap_through_it()
    {
        PositionResult result = Run(
            LimitBuy(""" "inputs": { "when": true, "price": 99, "size": 100 } """, ""),
            new Paths().Flat(100, 3).Minute(97, 97.5, 96.8, 97.2).Flat(97.2, 56).Build(withFunding: false),
            hours: 0);

        Assert.That(result.Cycles[0].Fills[0].Price, Is.EqualTo(99));
    }

    [Test]
    public void An_order_placed_after_a_fill_takes_part_from_the_next_minute()
    {
        PositionResult result = Run(Graphs.Ladder, new Paths().Minute(100, 101.5, 100, 100).Minute(100.5, 101.2, 100.4, 101).Flat(101, 58).Build(withFunding: false), hours: 0);

        CycleOutcome cycle = result.Cycles[0];
        Assert.That(cycle.Fills.Select(fill => (fill.Moment, fill.Action)), Is.EqualTo(new[] { (Paths.At(0), FillAction.Entry), (Paths.At(1), FillAction.Exit) }),
            "the sale at 101 is placed after the entry of 12:00, so the high of 12:00 does not fill it");
        Assert.That(cycle.Fills[1].Price, Is.EqualTo(101));
    }

    [Test]
    public void An_averaging_buy_and_a_sale_in_one_minute()
    {
        PositionResult result = Run(
            Graphs.Ladder,
            new Paths().Flat(100, 5).Minute(100, 101.2, 98.5, 100).Minute(100.2, 100.6, 100.1, 100.5).Flat(100.5, 53).Build(withFunding: false),
            hours: 0);

        CycleOutcome cycle = result.Cycles[0];
        double quantity = 1 + 100.0 / 99;
        Assert.That(cycle.Fills.Select(fill => (fill.Moment, fill.Action)), Is.EqualTo(new[]
        {
            (Paths.At(0), FillAction.Entry), (Paths.At(5), FillAction.Increase), (Paths.At(6), FillAction.Exit)
        }), "the buy fills first; the sale is placed again from the new average and waits for the next minute");
        Assert.That(cycle.Fills[2].Price, Is.EqualTo(200 / quantity * 1.01).Within(1e-9));
        Assert.That(cycle.Ambiguous, Is.True);
    }

    [Test]
    public void A_stop_and_an_averaging_buy_in_one_minute()
    {
        string json = WithPosition(
            $$"""{{EntryNodes}}, {{AverageNodes}}, { "id": "stop", "kind": "stop-loss", "inputs": { "distance": 0.05 } }""",
            $$"""{{EntryWires}}, {{AverageWires}}, { "from": "position.position", "to": "stop.position" }, {{Into("stop")}}""");

        PositionResult result = Run(json, new Paths().Flat(100, 3).Minute(96, 96, 94, 95.5).Flat(95.5, 56).Build(withFunding: false), hours: 0);

        CycleOutcome cycle = result.Cycles[0];
        Assert.That(cycle.Fills.Select(fill => fill.Action), Is.EqualTo(new[] { FillAction.Entry, FillAction.Exit }), "the averaging buy does not fill");
        Assert.That((cycle.ExitReason, cycle.Fills[^1].Moment, cycle.Ambiguous), Is.EqualTo((CycleExit.Stop, Paths.At(3), true)));
        Assert.That(cycle.Fills[^1].Price, Is.EqualTo(95), "the stop at 95 is better than the open of 96");
        Assert.That(result.Cycles[1].Fills[0].Moment, Is.EqualTo(Paths.At(4)), "a new cycle after the stop, from the next minute");
    }

    [Test]
    public void An_entry_and_an_exit_by_limit_orders_in_one_minute_make_the_cycle_ambiguous()
    {
        // Spec strategy-position «Порядок исполнений в минуте», «Вход и выход в одной минуте»: no position, a buy at 99 and a
        // sale at 101 whose prices do not change.
        string json = WithPosition(
            """
            { "id": "buy", "kind": "limit-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "when": true, "price": 99, "size": 100 } },
            { "id": "sell", "kind": "limit-order", "settings": { "side": "sell", "sizeIn": "amount" }, "inputs": { "when": true, "price": 101, "size": 1000 } }
            """,
            $$"""{{Into("buy")}}, {{Into("sell")}}""");

        PositionResult result = Run(json, new Paths().Flat(100, 5).Minute(100, 101.2, 98.9, 100).Flat(100, 54).Build(withFunding: false), hours: 0);

        CycleOutcome cycle = result.Cycles.Single();
        Assert.That(cycle.Fills.Select(fill => (fill.Moment, fill.Action, fill.Price)), Is.EqualTo(new[]
        {
            (Paths.At(5), FillAction.Entry, 99.0), (Paths.At(5), FillAction.Exit, 101.0)
        }), "the buy opens the cycle and the sale, left as it was, closes it in the same minute");
        Assert.That(cycle.Ambiguous, Is.True);
    }

    [Test]
    public void A_limit_entry_and_the_target_in_one_minute_make_the_cycle_ambiguous()
    {
        // Spec strategy-position «Порядок исполнений в минуте», «Лимитный вход и цель в одной минуте».
        string json = WithPosition(
            """
            { "id": "closed", "kind": "not" },
            { "id": "buy", "kind": "limit-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "price": 99, "size": 100 } },
            { "id": "target", "kind": "take-profit", "inputs": { "distance": 0.01 } }
            """,
            $$"""
              { "from": "position.open", "to": "closed.value" }, { "from": "closed.result", "to": "buy.when" }, {{Into("buy")}},
              { "from": "position.position", "to": "target.position" }, {{Into("target")}}
              """);

        PositionResult result = Run(json, new Paths().Flat(100, 5).Minute(100, 100.5, 98.9, 100).Flat(100, 54).Build(withFunding: false), hours: 0);

        CycleOutcome cycle = result.Cycles[0];
        Assert.That((cycle.ExitReason, cycle.Fills[^1].Moment, cycle.Fills[^1].Price), Is.EqualTo((CycleExit.Target, Paths.At(5), 99 * 1.01)));
        Assert.That(cycle.Ambiguous, Is.True);
    }

    [Test]
    public void A_market_entry_and_the_stop_in_one_minute_are_not_ambiguous_as_in_the_trade_of_a_plan()
    {
        // The entry by the market is at the open of the minute, so what follows in the minute follows it: the rule of a trade
        // of a plan (spec trade-plan-outcome), where only the stop and the target together are ambiguous.
        string json = WithPosition(
            $$"""{{EntryNodes}}, { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } }, { "id": "stop", "kind": "stop-loss", "inputs": { "distance": 0.01 } }""",
            $$"""
              {{EntryWires}}, { "from": "position.position", "to": "close-after.position" }, {{Into("close-after")}},
              { "from": "position.position", "to": "stop.position" }, {{Into("stop")}}
              """);
        MinutePath path = new Paths().Minute(100, 100.2, 98, 99).Flat(99, 5 * 60 - 1).Build(withFunding: false);

        PositionResult result = Run(json, path, hours: 4);

        CycleOutcome cycle = result.Cycles[0];
        TradeOutcome plan = CycleRun.Simulate(path, Paths.Start, new TradePlan(TradeSide.Long, TimeSpan.FromHours(4), stop: 0.01), Free, amount: 100).ToTrade();
        Assert.That((cycle.ExitReason, cycle.Ambiguous), Is.EqualTo((CycleExit.Stop, false)));
        Assert.That(CycleRunTests.Text(cycle.ToTrade()), Is.EqualTo(CycleRunTests.Text(plan)));
    }

    [Test]
    public void An_averaging_buy_increases_the_position_and_opens_no_new_cycle()
    {
        PositionResult result = Run(Graphs.Ladder, new Paths().Flat(100, 10).Minute(99.5, 99.6, 98.9, 99.2).Flat(99.2, 49).Build(withFunding: false), hours: 0);

        CycleOutcome cycle = result.Cycles.Single();
        Assert.That((cycle.Entries, cycle.MaxInvested, cycle.Open), Is.EqualTo((2, 200.0, true)));
    }

    [Test]
    public void The_stop_after_an_averaging_buy_is_counted_from_the_new_average()
    {
        // One averaging buy at 90 while less than 150 USDT is put in; the stop is 15 % below the average.
        string json = WithPosition(
            """
            { "id": "closed", "kind": "not" },
            { "id": "entry", "kind": "market-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "size": 100 } },
            { "id": "little", "kind": "compare", "settings": { "operation": "lt" }, "inputs": { "b": 150 } },
            { "id": "buy", "kind": "limit-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "price": 90, "size": 100 } },
            { "id": "stop", "kind": "stop-loss", "inputs": { "distance": 0.15 } }
            """,
            $$"""
              {{EntryWires}}, { "from": "position.invested", "to": "little.a" }, { "from": "little.result", "to": "buy.when" }, {{Into("buy")}},
              { "from": "position.position", "to": "stop.position" }, {{Into("stop")}}
              """);
        double average = 200 / (1 + 100.0 / 90);
        double newStop = average * (1 - 0.15);

        PositionResult result = Run(
            json,
            new Paths().Flat(100, 5).Minute(91, 91, 89.9, 90.5).Flat(90.5, 3).Minute(86, 86, 84, 85).Flat(85, 2)
                .Minute(82, 82, newStop - 0.01, 81).Flat(81, 47).Build(withFunding: false),
            hours: 0);

        CycleOutcome cycle = result.Cycles[0];
        Assert.That(newStop, Is.LessThan(84), "the stop of the first entry alone, 85, would have closed at 12:09");
        Assert.That(cycle.Fills.Select(fill => fill.Action), Is.EqualTo(new[] { FillAction.Entry, FillAction.Increase, FillAction.Exit }));
        Assert.That((cycle.ExitReason, cycle.Fills[^1].Moment), Is.EqualTo((CycleExit.Stop, Paths.At(12))));
        Assert.That(cycle.Fills[^1].Price, Is.EqualTo(newStop).Within(1e-9));
    }

    [Test]
    public void The_time_of_a_cycle_with_averaging_runs_from_the_order_that_opened_it()
    {
        string json = WithPosition(
            $$"""{{EntryNodes}}, {{AverageNodes}}, { "id": "close-after", "kind": "close-after", "inputs": { "after": "1d" } }""",
            $$"""{{EntryWires}}, {{AverageWires}}, { "from": "position.position", "to": "close-after.position" }, {{Into("close-after")}}""");

        PositionResult result = Run(
            json,
            new Paths().Flat(100, 6 * 60).Minute(99.5, 99.6, 98.9, 99.2).Flat(99.2, 19 * 60 - 1).Build(withFunding: false),
            hours: 24);

        CycleOutcome cycle = result.Cycles[0];
        Assert.That(cycle.Fills.Select(fill => (fill.Moment, fill.Action)), Is.EqualTo(new[]
        {
            (Paths.At(0), FillAction.Entry), (Paths.At(6 * 60), FillAction.Increase), (Paths.At(24 * 60), FillAction.Exit)
        }));
        Assert.That(cycle.ExitReason, Is.EqualTo(CycleExit.Time));
        Assert.That(cycle.Fills[^1].Quantity, Is.EqualTo(1 + 100.0 / 99).Within(1e-12), "the whole position");
        Assert.That(result.Orders.Count(order => order.Node == "buy"), Is.EqualTo(1), "the buy standing at 98.01 is taken off with the time");
    }

    [Test]
    public void A_cycle_of_one_entry_closed_by_its_time_is_the_trade_of_its_plan()
    {
        TradeCosts costs = TradeCosts.Default;
        string json = WithPosition(
            $$"""{{EntryNodes}}, { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } }""",
            $$"""{{EntryWires}}, { "from": "position.position", "to": "close-after.position" }, {{Into("close-after")}}""");
        MinutePath path = new Paths().Flat(100, 30).Minute(100, 101, 99, 100.7).Flat(100.7, 6 * 60).Payment(150, 0.0003).Build();

        PositionResult result = Run(json, path, hours: 4, costs);

        CycleOutcome cycle = result.Cycles[0];
        TradeOutcome plan = CycleRun.Simulate(path, Paths.Start, new TradePlan(TradeSide.Long, TimeSpan.FromHours(4)), costs, amount: 100).ToTrade();
        Assert.That(CycleRunTests.Text(cycle.ToTrade()), Is.EqualTo(CycleRunTests.Text(plan)));
        Assert.That(cycle.NetAmount, Is.EqualTo(cycle.Net * 100));
    }

    [Test]
    public void A_cycle_open_at_the_end_is_an_estimate_at_the_last_close()
    {
        PositionResult result = Run(Graphs.Ladder, new Paths().Flat(100, 30).Minute(99.5, 99.6, 98.9, 99.2).Flat(99.4, 29).Build(withFunding: false), hours: 0);

        CycleOutcome cycle = result.Cycles.Single();
        double expected = 0.5 * (99.4 / 100 - 1) + 0.5 * (99.4 / 99 - 1);
        Assert.That(cycle.Open, Is.True);
        Assert.That(cycle.Net, Is.EqualTo(expected).Within(1e-12));
        Assert.That(cycle.ExitReason, Is.EqualTo(CycleExit.None));
    }

    [Test]
    public void A_hole_while_the_position_is_open_leaves_the_cycle_out_and_the_run_goes_on_closed()
    {
        Paths paths = new Paths().Flat(100, 10);
        for (int minute = 0; minute < 10; minute++)
        {
            paths.Unknown();
        }

        PositionResult result = Run(Graphs.Ladder, paths.Flat(100, 40).Build(withFunding: false), hours: 0);

        Assert.That(result.Cycles[0].Status, Is.EqualTo(TradeStatus.Coverage));
        Assert.That(result.Cycles[1].Fills[0].Moment, Is.EqualTo(Paths.At(21)), "the computation at the first minute after the hole places the entry");
        Assert.That(result.Cycles[1].Status, Is.EqualTo(TradeStatus.Ok));
    }

    [Test]
    public void The_time_closes_the_cycle_before_the_graph_of_the_close_it_ran_out_at()
    {
        // Spec strategy-position «Одна позиция на пару», «Вход в момент закрытия у стратегии с заявками»: events at 13:00 and
        // 17:00, the time is 4h.
        PositionResult result = Run(Graphs.Raised(SignalOnTheSecondCandle("long", "4h")), new Paths().Flat(100, 10 * 60).Build(withFunding: false), hours: 9, close: TwoEvents);

        Assert.That(result.Orders.Select(order => (order.Moment, order.Result)), Is.EqualTo(new[] { (Paths.At(60), OrderResult.Filled), (Paths.At(5 * 60), OrderResult.Filled) }),
            "the order of 17:00 is not skipped: the cycle of 13:00 is closed by then");
        Assert.That(result.Cycles.Select(cycle => (cycle.Fills[0].Moment, cycle.Fills[^1].Moment, cycle.ExitReason)), Is.EqualTo(new[]
        {
            (Paths.At(60), Paths.At(5 * 60), CycleExit.Time), (Paths.At(5 * 60), Paths.At(9 * 60), CycleExit.Time)
        }));
    }

    [Test]
    public void A_time_that_runs_out_at_the_end_of_the_history_closes_the_cycle()
    {
        // Spec strategy-position «Срок у края истории», «Срок кончается в конце истории».
        PositionResult result = Run(TimedEntry("4h"), new Paths().Flat(100, 3 * 60).Flat(101, 60).Build(withFunding: false), hours: 3);

        CycleOutcome cycle = result.Cycles.Single();
        Assert.That((cycle.Open, cycle.ExitReason, cycle.Fills[^1].Moment, cycle.Fills[^1].Price), Is.EqualTo((false, CycleExit.Time, Paths.At(4 * 60), 101.0)));
    }

    [Test]
    public void A_time_that_runs_out_where_a_hole_starts_closes_the_cycle()
    {
        // Spec strategy-position «Срок у края истории», «Дыра начинается в момент срока».
        Paths paths = new Paths().Flat(100, 4 * 60);
        for (int minute = 0; minute < 60; minute++)
        {
            paths.Unknown();
        }

        PositionResult result = Run(TimedEntry("4h"), paths.Flat(100, 60).Build(withFunding: false), hours: 5);

        CycleOutcome cycle = result.Cycles[0];
        Assert.That((cycle.Status, cycle.ExitReason, cycle.Fills[^1].Moment), Is.EqualTo((TradeStatus.Ok, CycleExit.Time, Paths.At(4 * 60))));
    }

    [Test]
    public void A_limit_order_whose_cycle_would_end_after_the_end_of_the_history_gives_no_cycle()
    {
        // Spec strategy-preview «Только разведка», «Срок за концом разведки у стратегии с заявками»: the time is a day, the
        // price is reached two hours before the end.
        string json = WithPosition(
            """
            { "id": "buy", "kind": "limit-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "when": true, "price": 99, "size": 100 } },
            { "id": "close-after", "kind": "close-after", "inputs": { "after": "1d" } }
            """,
            $$"""{{Into("buy")}}, { "from": "position.position", "to": "close-after.position" }, {{Into("close-after")}}""");

        PositionResult result = Run(json, new Paths().Flat(100, 24 * 60).Minute(99.5, 99.6, 98.9, 99.2).Flat(99.2, 2 * 60 - 1).Build(withFunding: false), hours: 25);

        Assert.That(result.Cycles, Is.Empty);
        Assert.That(result.Orders.Single(), Is.EqualTo(new OrderOutcome(Paths.At(24 * 60), "buy", OrderSide.Buy, OrderResult.Excluded, OrderExclusion.Period, null, null, null)));
    }

    [Test]
    public void An_entry_of_a_signal_whose_time_ends_after_the_end_of_the_history_gives_no_cycle()
    {
        PositionResult result = Run(Graphs.Raised(SignalOnTheSecondCandle("long", "4h")), new Paths().Flat(100, 4 * 60).Build(withFunding: false), hours: 3, close: Rising);

        Assert.That(result.Cycles, Is.Empty);
        Assert.That(result.Orders.Single(), Is.EqualTo(new OrderOutcome(Paths.At(60), "enter", OrderSide.Long, OrderResult.Excluded, OrderExclusion.Period, null, null, null)));
    }

    [Test]
    public void The_time_of_a_cycle_a_limit_order_opened_runs_from_its_fill()
    {
        // Spec strategy-position «Стоп, цель и срок при нескольких входах», «Срок лимитного входа»: placed at 12:00 of the
        // first day, filled at 15:00 of the third, closed at 15:00 of the fourth. Funding is paid at 18:00 of the second day
        // (before the entry), at 00:00 of the fourth (inside the cycle) and after its exit.
        string json = WithPosition(
            """
            { "id": "closed", "kind": "not" },
            { "id": "buy", "kind": "limit-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "price": 99, "size": 100 } },
            { "id": "close-after", "kind": "close-after", "inputs": { "after": "1d" } }
            """,
            $$"""
              { "from": "position.open", "to": "closed.value" }, { "from": "closed.result", "to": "buy.when" }, {{Into("buy")}},
              { "from": "position.position", "to": "close-after.position" }, {{Into("close-after")}}
              """);
        MinutePath path = new Paths().Flat(100, 51 * 60).Minute(99.5, 99.6, 98.9, 99.2).Flat(99.2, 25 * 60 - 1)
            .Payment(30 * 60, 0.0001).Payment(60 * 60, 0.0003).Payment(75 * 60 + 30, 0.0005).Build();
        var costs = new TradeCosts(TakerFee: 0, Slippage: 0, Funding: true, MakerFee: 0);

        PositionResult result = Run(json, path, hours: 75, costs);

        CycleOutcome cycle = result.Cycles[0];
        Assert.That(cycle.Fills.Select(fill => (fill.Moment, fill.Action)), Is.EqualTo(new[] { (Paths.At(51 * 60), FillAction.Entry), (Paths.At(75 * 60), FillAction.Exit) }),
            "the exit is a day after the fill, never before it");
        Assert.That(cycle.ExitReason, Is.EqualTo(CycleExit.Time));
        Assert.That(cycle.Funding, Is.EqualTo(-0.0003 * 99.2 / 99).Within(1e-15), "the long pays the one payment between its entry and its exit");
    }

    [Test]
    public void An_entry_of_a_signal_that_met_no_minute_with_trades_within_its_time_gives_no_trade()
    {
        // Spec strategy-position «Стоп, цель и срок при нескольких входах», «Вход не исполнился за срок»: the order of 13:00
        // with the time 4h, no trades before 17:00.
        PositionResult result = Run(Graphs.Raised(SignalOnTheSecondCandle("long", "4h")), new Paths().Flat(100, 60).Quiet(4 * 60).Flat(100, 5 * 60).Build(withFunding: false), hours: 9, close: Rising);

        Assert.That(result.Cycles, Is.Empty);
        Assert.That(result.Orders.Single(), Is.EqualTo(new OrderOutcome(Paths.At(60), "enter", OrderSide.Long, OrderResult.Excluded, OrderExclusion.NoTrade, null, null, null)));
    }

    [Test]
    public void A_market_order_that_cannot_open_a_cycle_is_refused_once_in_a_candle()
    {
        // Spec strategy-position «Недопустимые выходы у заявки по условию», «Стоп вне границ у входа по условию»: the
        // condition is true with the position closed all along, the stop is 1.2. Two hourly candles, 120 minutes.
        string json = WithPosition(
            $$"""{{EntryNodes}}, { "id": "stop", "kind": "stop-loss", "inputs": { "distance": 1.2 } }""",
            $$"""{{EntryWires}}, { "from": "position.position", "to": "stop.position" }, {{Into("stop")}}""");

        PositionResult result = Run(json, new Paths().Flat(100, 120).Build(withFunding: false), hours: 1);

        Assert.That(result.Cycles, Is.Empty);
        Assert.That(result.Orders, Is.EqualTo(new[]
        {
            new OrderOutcome(Paths.At(0), "entry", OrderSide.Buy, OrderResult.Excluded, OrderExclusion.Plan, null, null, null),
            new OrderOutcome(Paths.At(60), "entry", OrderSide.Buy, OrderResult.Excluded, OrderExclusion.Plan, null, null, null)
        }), "one refusal of the node in each candle, not one in each minute");
    }

    [Test]
    public void A_market_order_whose_cycle_would_end_after_the_end_is_refused_once_in_a_candle()
    {
        PositionResult result = Run(TimedEntry("4h"), new Paths().Flat(100, 120).Build(withFunding: false), hours: 1);

        Assert.That(result.Cycles, Is.Empty);
        Assert.That(result.Orders.Select(order => (order.Moment, order.Exclusion)), Is.EqualTo(new[]
        {
            (Paths.At(0), (OrderExclusion?)OrderExclusion.Period), (Paths.At(60), OrderExclusion.Period)
        }));
    }

    [Test]
    public void An_order_of_close_position_is_taken_off_when_the_position_closed_another_way()
    {
        // Spec strategy-execution «Закрытие позиции по условию», «Позиция закрылась раньше заявки». At 12:05 the averaging
        // buy fills, the computation after it places the order of close-position (more than 150 USDT is put in), and the
        // sale at 101, left as it was, closes the position in the same minute. The next cycle opens at 12:06.
        string json = WithPosition(
            $$"""
              {{EntryNodes}},
              { "id": "buy", "kind": "limit-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "price": 99, "size": 100 } },
              { "id": "sell", "kind": "limit-order", "settings": { "side": "sell", "sizeIn": "amount" }, "inputs": { "price": 101, "size": 100000 } },
              { "id": "much", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 150 } },
              { "id": "out", "kind": "close-position" }
              """,
            $$"""
              {{EntryWires}}, { "from": "position.open", "to": "buy.when" }, {{Into("buy")}}, { "from": "position.open", "to": "sell.when" }, {{Into("sell")}},
              { "from": "position.invested", "to": "much.a" }, { "from": "much.result", "to": "out.when" }, {{Into("out")}}
              """);

        PositionResult result = Run(json, new Paths().Flat(100, 5).Minute(100, 101.2, 98.9, 100).Flat(100, 54).Build(withFunding: false), hours: 0);

        Assert.That(result.Cycles[0].Fills.Select(fill => (fill.Moment, fill.Node)), Is.EqualTo(new[] { (Paths.At(0), "entry"), (Paths.At(5), "buy"), (Paths.At(5), "sell") }));
        Assert.That(result.Cycles[1].Fills.Select(fill => (fill.Moment, fill.Node)), Is.EqualTo(new[] { (Paths.At(6), "entry") }), "the order of the first cycle does not close the second");
        Assert.That(result.Cycles[1].Open, Is.True);
        Assert.That(result.Orders.Where(order => order.Node == "out"), Is.Empty);
    }

    [Test]
    public void A_cycle_of_twenty_thousand_fills_is_counted_within_two_seconds()
    {
        // Task 11.6: the quantity and the money put in are running sums, so the state of the position after each fill does
        // not cost a pass over all the entries. A market buy in every minute of 20 000.
        string json = WithPosition(
            """{ "id": "always", "kind": "market-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "when": true, "size": 100 } }""",
            Into("always"));
        MinutePath path = new Paths().Flat(100, 20_000).Build(withFunding: false);
        TradingStrategy strategy = Strategy(json);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var run = new PositionRun(strategy, path, Free, Paths.Start);
        run.Step(Hour(0), lastFunding: null);
        PositionResult result = run.Finish(Paths.At(20_000));
        watch.Stop();

        CycleOutcome cycle = result.Cycles.Single();
        Assert.That((cycle.Entries, cycle.MaxInvested), Is.EqualTo((20_000, 2_000_000.0)));
        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void A_cancelled_run_stops_at_its_next_minute()
    {
        using var cancellation = new CancellationTokenSource();
        var run = new PositionRun(Strategy(Graphs.Ladder), new Paths().Flat(100, 120).Build(withFunding: false), Free, Paths.Start, cancellationToken: cancellation.Token);
        run.Step(Hour(0), lastFunding: null);
        run.Advance(Paths.At(30));

        cancellation.Cancel();

        Assert.That(() => run.Advance(Paths.At(60)), Throws.InstanceOf<OperationCanceledException>());
    }

    // Buys 100 USDT by the market while there is no position; the cycle closes by its time.
    private static string TimedEntry(string after)
    {
        return WithPosition(
            $$"""{{EntryNodes}}, { "id": "close-after", "kind": "close-after", "inputs": { "after": "{{after}}" } }""",
            $$"""{{EntryWires}}, { "from": "position.position", "to": "close-after.position" }, {{Into("close-after")}}""");
    }

    // close > 100 for the first time: the closes 100, 101 give the event at 13:00; the entry and its close-after.
    private static string SignalOnTheSecondCandle(string side, string after)
    {
        return $$"""
                 {
                   "formatVersion": 1,
                   "nodes": [
                     { "id": "c", "kind": "candle" },
                     { "id": "above", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 100 } },
                     { "id": "first", "kind": "first-time" },
                     { "id": "enter", "kind": "enter", "settings": { "side": "{{side}}" } },
                     { "id": "close-after", "kind": "close-after", "inputs": { "after": "{{after}}" } }
                   ],
                   "wires": [
                     { "from": "c.close", "to": "above.a" }, { "from": "above.result", "to": "first.value" }, { "from": "first.event", "to": "enter.when" }
                   ]
                 }
                 """;
    }

    private static decimal Rising(int hour)
    {
        return 100 + hour;
    }

    // Above 100 at 13:00 and at 17:00 only: two events of the signal.
    private static decimal TwoEvents(int hour)
    {
        return hour is 1 or 5 ? 101 : 100;
    }

    private static string LimitBuy(string inputs, string wires, string nodes = "", string nodeWires = "")
    {
        return WithPosition(
            $$"""{{nodes}} { "id": "buy", "kind": "limit-order", "settings": { "side": "buy", "sizeIn": "amount" }, {{inputs}} }""",
            $$"""{{nodeWires}} {{(wires.Length > 0 ? wires + "," : "")}} {{Into("buy")}}""");
    }
}
