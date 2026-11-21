using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Tests.Unit.Graph;

namespace ProfitFox.Tests.Unit.Execution;

/// <summary>
///     Spec strategy-execution «Узел входа», «Узлы выхода» and «Несколько узлов входа на одной свече» (change
///     add-strategy-graph, task 2.1): what the executor of a strategy gives at each closed candle.
/// </summary>
public sealed class StrategyExecutionTests
{
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);

    // close > 0.02 for the first time: closes of 0.01 and 0.03 give no event and an event.
    private const string CloseAboveNodes =
        """
        { "id": "c", "kind": "candle" },
        { "id": "above", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 0.02 } },
        { "id": "first", "kind": "first-time" }
        """;

    private const string CloseAboveWires =
        """
        { "from": "c.close", "to": "above.a" },
        { "from": "above.result", "to": "first.value" }
        """;

    [Test]
    public void An_order_comes_at_the_close_of_the_candle_of_the_event()
    {
        var executor = new StrategyExecutor(Strategy(Graphs.H0001Strategy));
        ClosedCandle first = History.Candle(index: 1, close: 1);
        ClosedCandle second = History.Candle(index: 2, close: 1);

        executor.Step("BTCUSDT", first, new FundingPayment(first.CloseTime, 0.0001m));
        StrategyStep step = executor.Step("BTCUSDT", second, new FundingPayment(second.CloseTime, 0.0006m));

        Assert.That(second.OpenTime, Is.EqualTo(History.Start.AddHours(8)));
        Assert.That(step.Entry, Is.EqualTo(EntrySide.Short));
        Assert.That(step.Moment, Is.EqualTo(History.Start.AddHours(12)));
    }

    [Test]
    public void A_candle_without_the_event_gives_no_order()
    {
        var executor = new StrategyExecutor(Strategy(Graphs.H0001Strategy));
        ClosedCandle candle = History.Candle(index: 1, close: 1);

        StrategyStep step = executor.Step("BTCUSDT", candle, new FundingPayment(candle.CloseTime, 0.0001m));

        Assert.That(step.Entry, Is.Null);
        Assert.That(step.Moment, Is.EqualTo(candle.CloseTime));
    }

    [Test]
    public void A_stop_wired_from_a_parameter_is_carried_by_the_order()
    {
        string json = Graph(
            """{ "name": "stop", "type": "number", "value": 0.02 }""",
            $$"""
              { "id": "p", "kind": "parameters" },
              {{CloseAboveNodes}},
              { "id": "enter", "kind": "enter", "settings": { "side": "long" } },
              { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } },
              { "id": "stop-loss", "kind": "stop-loss" }
              """,
            $$"""
              {{CloseAboveWires}},
              { "from": "first.event", "to": "enter.when" },
              { "from": "p.stop", "to": "stop-loss.distance" }
              """);

        StrategyStep order = Steps(json, 0.01m, 0.03m)[1];

        Assert.That(order.Entry, Is.EqualTo(EntrySide.Long));
        Assert.That(order.Stop, Is.EqualTo(ExitDistance.Of(0.02m)));
        Assert.That(order.Target, Is.EqualTo(ExitDistance.None));
    }

    [Test]
    public void A_stop_that_changes_from_candle_to_candle_is_taken_at_the_candle_of_the_order()
    {
        string json = Graph(
            "",
            $$"""
              {{CloseAboveNodes}},
              { "id": "enter", "kind": "enter", "settings": { "side": "long" } },
              { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } },
              { "id": "stop-loss", "kind": "stop-loss" }
              """,
            $$"""
              {{CloseAboveWires}},
              { "from": "first.event", "to": "enter.when" },
              { "from": "c.close", "to": "stop-loss.distance" }
              """);

        List<StrategyStep> steps = Steps(json, 0.01m, 0.03m);

        Assert.That(steps.Select(step => step.Stop.Value), Is.EqualTo(new decimal?[] { 0.01m, 0.03m }));
        Assert.That(steps[0].Entry, Is.Null);
        Assert.That(steps[1].Entry, Is.EqualTo(EntrySide.Long));
        Assert.That(steps[1].Stop, Is.EqualTo(ExitDistance.Of(0.03m)));
    }

    [Test]
    public void A_candle_without_the_event_still_gives_the_time_the_stop_and_the_target()
    {
        string json = Graph(
            "",
            $$"""
              {{CloseAboveNodes}},
              { "id": "enter", "kind": "enter", "settings": { "side": "short" } },
              { "id": "close-after", "kind": "close-after", "inputs": { "after": "1d" } },
              { "id": "stop-loss", "kind": "stop-loss", "inputs": { "distance": 0.02 } },
              { "id": "take-profit", "kind": "take-profit", "inputs": { "distance": 0.04 } }
              """,
            $$"""
              {{CloseAboveWires}},
              { "from": "first.event", "to": "enter.when" }
              """);

        StrategyStep step = Steps(json, 0.01m)[0];

        Assert.That(step.Entry, Is.Null);
        Assert.That(step.After, Is.EqualTo(TimeSpan.FromDays(1)));
        Assert.That(step.Stop, Is.EqualTo(ExitDistance.Of(0.02m)));
        Assert.That(step.Target, Is.EqualTo(ExitDistance.Of(0.04m)));
    }

    [Test]
    public void A_stop_that_is_not_computed_yet_is_in_the_strategy_but_has_no_value()
    {
        string json = Graph(
            "",
            $$"""
              {{CloseAboveNodes}},
              { "id": "prev", "kind": "previous" },
              { "id": "enter", "kind": "enter", "settings": { "side": "long" } },
              { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } },
              { "id": "stop-loss", "kind": "stop-loss" }
              """,
            $$"""
              {{CloseAboveWires}},
              { "from": "first.event", "to": "enter.when" },
              { "from": "c.close", "to": "prev.value" },
              { "from": "prev.result", "to": "stop-loss.distance" }
              """);

        List<StrategyStep> steps = Steps(json, 0.01m, 0.03m);

        Assert.That(steps[0].Stop.InStrategy, Is.True);
        Assert.That(steps[0].Stop.Value, Is.Null, "the previous close is missing at the first candle");
        Assert.That(steps[1].Stop, Is.EqualTo(ExitDistance.Of(0.01m)));
    }

    [Test]
    public void The_strategy_of_H0001_has_a_time_and_no_stop_and_no_target()
    {
        var executor = new StrategyExecutor(Strategy(Graphs.H0001Strategy));
        ClosedCandle first = History.Candle(index: 1, close: 1);
        ClosedCandle second = History.Candle(index: 2, close: 1);

        executor.Step("BTCUSDT", first, new FundingPayment(first.CloseTime, 0.0001m));
        StrategyStep order = executor.Step("BTCUSDT", second, new FundingPayment(second.CloseTime, 0.0006m));

        Assert.That(order, Is.EqualTo(new StrategyStep(second.CloseTime, EntrySide.Short, FourHours, ExitDistance.None, ExitDistance.None)));
        Assert.That(order.Stop.InStrategy, Is.False);
        Assert.That(order.Target.InStrategy, Is.False);
    }

    [Test]
    public void A_long_and_a_short_on_one_candle_give_no_order()
    {
        string json = TwoEntries("long", "short");

        List<StrategyStep> steps = Steps(json, 0.01m, 0.03m);

        Assert.That(steps.Select(step => step.Entry), Is.EqualTo(new EntrySide?[] { null, null }));
        Assert.That(steps[1].After, Is.EqualTo(FourHours), "the values of the candle are still given");
    }

    [Test]
    public void Two_entries_of_one_side_on_one_candle_give_one_order_of_that_side()
    {
        string json = TwoEntries("long", "long");

        List<StrategyStep> steps = Steps(json, 0.01m, 0.03m);

        Assert.That(steps.Select(step => step.Entry), Is.EqualTo(new EntrySide?[] { null, EntrySide.Long }));
    }

    [Test]
    public void Only_the_entry_whose_event_happened_gives_the_order()
    {
        // The long enters when the close first goes above 0.02, the short when it first is not above it.
        string json = Graph(
            "",
            $$"""
              {{CloseAboveNodes}},
              { "id": "below", "kind": "not" },
              { "id": "first-below", "kind": "first-time" },
              { "id": "enter-long", "kind": "enter", "settings": { "side": "long" } },
              { "id": "enter-short", "kind": "enter", "settings": { "side": "short" } },
              { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } }
              """,
            $$"""
              {{CloseAboveWires}},
              { "from": "above.result", "to": "below.value" },
              { "from": "below.result", "to": "first-below.value" },
              { "from": "first.event", "to": "enter-long.when" },
              { "from": "first-below.event", "to": "enter-short.when" }
              """);

        List<StrategyStep> steps = Steps(json, 0.01m, 0.03m, 0.01m);

        Assert.That(steps.Select(step => step.Entry), Is.EqualTo(new EntrySide?[] { null, EntrySide.Long, EntrySide.Short }));
    }

    [Test]
    public void Before_the_candles_of_a_pair_only_the_written_values_are_known()
    {
        string json = Graph(
            """{ "name": "stop", "type": "number", "value": 0.02 }""",
            $$"""
              { "id": "p", "kind": "parameters" },
              {{CloseAboveNodes}},
              { "id": "enter", "kind": "enter", "settings": { "side": "long" } },
              { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } },
              { "id": "stop-loss", "kind": "stop-loss" },
              { "id": "take-profit", "kind": "take-profit", "inputs": { "distance": 0.04 } }
              """,
            $$"""
              {{CloseAboveWires}},
              { "from": "first.event", "to": "enter.when" },
              { "from": "p.stop", "to": "stop-loss.distance" }
              """);
        var executor = new StrategyExecutor(Strategy(json));

        StrategyStep step = executor.BeforeCandles(History.Start);

        Assert.That(step, Is.EqualTo(new StrategyStep(History.Start, Entry: null, FourHours, ExitDistance.Of(null), ExitDistance.Of(0.04m))));
    }

    [Test]
    public void Candles_of_a_pair_go_in_the_order_of_their_close()
    {
        var executor = new StrategyExecutor(Strategy(Graphs.H0001Strategy));
        executor.Step("BTCUSDT", History.Candle(1, 1), lastFunding: null);

        Assert.That(() => executor.Step("BTCUSDT", History.Candle(0, 1), lastFunding: null), Throws.ArgumentException);
    }

    // Two enter nodes on the same event: the close first above 0.02.
    private static string TwoEntries(string first, string second)
    {
        return Graph(
            "",
            $$"""
              {{CloseAboveNodes}},
              { "id": "enter-a", "kind": "enter", "settings": { "side": "{{first}}" } },
              { "id": "enter-b", "kind": "enter", "settings": { "side": "{{second}}" } },
              { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } }
              """,
            $$"""
              {{CloseAboveWires}},
              { "from": "first.event", "to": "enter-a.when" },
              { "from": "first.event", "to": "enter-b.when" }
              """);
    }

    private static string Graph(string parameters, string nodes, string wires)
    {
        return $$"""
                 {
                   "formatVersion": 1,
                   "parameters": [{{parameters}}],
                   "outputs": [],
                   "nodes": [{{nodes}}],
                   "wires": [{{wires}}]
                 }
                 """;
    }

    private static TradingStrategy Strategy(string json)
    {
        ReadResult<TradingStrategy> result = StrategyGraph.ReadStrategy(json);
        Assert.That(result.Errors, Is.Empty, "the test strategy must pass the checks");
        return result.Value!;
    }

    private static List<StrategyStep> Steps(string json, params decimal[] closes)
    {
        var executor = new StrategyExecutor(Strategy(json));
        return [.. History.Closes(closes).Select(candle => executor.Step("BTCUSDT", candle, lastFunding: null))];
    }
}
