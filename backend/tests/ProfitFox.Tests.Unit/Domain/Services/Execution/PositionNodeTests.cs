using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Positions;

namespace ProfitFox.Tests.Unit.Domain.Services.Execution;

/// <summary>
///     Spec strategy-execution of change add-position-node (tasks 3.1, 3.2 and 3.3): the arithmetic nodes, the node
///     <c>position</c> as a source of the state of the position, and the computation again after a fill, which does not
///     move the memory of «предыдущее» and «впервые».
/// </summary>
public sealed class PositionNodeTests
{
    // A market order that never fires gives the orders of the position their wire.
    private const string Orders =
        """
        { "id": "position", "kind": "position" },
        { "id": "never", "kind": "market-order", "settings": { "side": "buy", "sizeIn": "amount" }, "inputs": { "when": false, "size": 100 } }
        """;

    private const string OrdersWire = """{ "from": "never.order", "to": "position.orders" }""";

    private static readonly DateTimeOffset Monday = History.Start;

    [TestCase("multiply", "99.5", "1.01", "100.495")]
    [TestCase("add", "0.1", "0.2", "0.3")]
    [TestCase("subtract", "1", "0.01", "0.99")]
    public void The_arithmetic_is_exact_in_decimals(string kind, string a, string b, string result)
    {
        string json = History.WithOutput("number", "x.result", $$"""{ "id": "x", "kind": "{{kind}}", "inputs": { "a": {{a}}, "b": {{b}} } }""");

        PinValue x = History.Run(json, History.Closes(1))[0][0];

        Assert.That(x.Number, Is.EqualTo(decimal.Parse(result, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [TestCase("multiply", "79000000000000000000000000000", "10")]
    [TestCase("add", "79000000000000000000000000000", "79000000000000000000000000000")]
    [TestCase("subtract", "-79000000000000000000000000000", "79000000000000000000000000000")]
    public void A_result_too_large_for_a_decimal_is_missing(string kind, string a, string b)
    {
        string json = History.WithOutput("number", "x.result", $$"""{ "id": "x", "kind": "{{kind}}", "inputs": { "a": {{a}}, "b": {{b}} } }""");

        PinValue x = History.Run(json, History.Closes(1))[0][0];

        Assert.That(x.IsMissing, Is.True, "a decimal holds up to 7.9e28; past it there is no value, not an exception");
    }

    [Test]
    public void A_missing_input_gives_a_missing_result()
    {
        string json = History.WithOutput(
            "number",
            "x.result",
            $$"""{{Orders}}, { "id": "x", "kind": "multiply", "inputs": { "b": 1.01 } }""",
            $$"""{{OrdersWire}}, { "from": "position.averagePrice", "to": "x.a" }""");

        PinValue x = History.Run(json, History.Closes(1))[0][0];

        Assert.That(x.IsMissing, Is.True, "the average price of a closed position is missing");
    }

    [Test]
    public void After_two_entries()
    {
        decimal quantity = 1m + 100m / 99m;
        var state = new PositionState(quantity, Invested: 200m, AveragePrice: 200m / quantity, LastEntryPrice: 99m, LastEntryTime: Monday.AddHours(12).AddMinutes(7));
        var run = new GraphRun(History.Graph(StateGraph()));

        PinValue[] outputs = [.. run.Step(History.Candle(3, 100), facts: null, state)];

        Assert.That(outputs[0], Is.EqualTo(PinValue.FromBoolean(true)));
        Assert.That(outputs[1].Number, Is.EqualTo(quantity));
        Assert.That(outputs[2].Number, Is.EqualTo(200m / quantity));
        Assert.That(outputs[3].Number, Is.EqualTo(99m));
        Assert.That(outputs[4].Number, Is.EqualTo(200m));
    }

    [Test]
    public void The_time_since_the_last_entry()
    {
        var state = new PositionState(1m, Invested: 100m, AveragePrice: 100m, LastEntryPrice: 100m, LastEntryTime: Monday.AddHours(12).AddMinutes(7));
        var run = new GraphRun(History.Graph(StateGraph()));
        ClosedCandle candle = History.Candle(3, 100);

        PinValue since = run.Step(candle, facts: null, state)[5];

        Assert.That(candle.CloseTime, Is.EqualTo(Monday.AddHours(16)));
        Assert.That(since.Duration, Is.EqualTo(new TimeSpan(3, 53, 0)));
    }

    [Test]
    public void A_closed_position()
    {
        var run = new GraphRun(History.Graph(StateGraph()));

        PinValue[] outputs = [.. run.Step(History.Candle(0, 100), facts: null, PositionState.Closed)];

        Assert.That(outputs[0], Is.EqualTo(PinValue.FromBoolean(false)));
        Assert.That(outputs[1].Number, Is.Zero);
        Assert.That(outputs[2].IsMissing, Is.True);
        Assert.That(outputs[3].IsMissing, Is.True);
        Assert.That(outputs[4].Number, Is.Zero);
        Assert.That(outputs[5].IsMissing, Is.True);
        Assert.That(run.Step(History.Candle(1, 100), facts: null)[0], Is.EqualTo(PinValue.FromBoolean(false)), "a step without a state is a closed position");
    }

    [Test]
    public void The_computation_again_sees_the_new_state_at_the_moment_of_the_fill()
    {
        var run = new GraphRun(History.Graph(StateGraph()));
        run.Step(History.Candle(3, 100), facts: null, PositionState.Closed);
        var bought = new PositionState(1m, Invested: 100m, AveragePrice: 100m, LastEntryPrice: 100m, LastEntryTime: Monday.AddHours(16).AddMinutes(3));

        PinValue[] outputs = [.. run.Again(bought, Monday.AddHours(16).AddMinutes(5))];

        Assert.That(outputs[0], Is.EqualTo(PinValue.FromBoolean(true)));
        Assert.That(outputs[5].Duration, Is.EqualTo(TimeSpan.FromMinutes(2)));
    }

    [Test]
    public void Previous_does_not_move_on_the_computation_again()
    {
        string json = History.WithOutput("number", "prev.result", """{ "id": "c", "kind": "candle" }, { "id": "prev", "kind": "previous" }""", """{ "from": "c.close", "to": "prev.value" }""");
        var run = new GraphRun(History.Graph(json));
        run.Step(History.Candle(0, 1), facts: null);
        Assert.That(run.Step(History.Candle(1, 2), facts: null)[0].Number, Is.EqualTo(1m));

        Assert.That(run.Again(PositionState.Closed, History.Candle(1, 2).CloseTime.AddMinutes(7))[0].Number, Is.EqualTo(1m), "still the close of the first candle");
        Assert.That(run.Again(PositionState.Closed, History.Candle(1, 2).CloseTime.AddMinutes(9))[0].Number, Is.EqualTo(1m));
        Assert.That(run.Step(History.Candle(2, 3), facts: null)[0].Number, Is.EqualTo(2m), "the close of the second candle, however many computations again");
    }

    [Test]
    public void First_time_on_the_computation_again_compares_with_the_last_close_and_keeps_it()
    {
        string json = History.WithOutput(
            "event",
            "first.event",
            $$"""{{Orders}}, { "id": "closed", "kind": "not" }, { "id": "first", "kind": "first-time" }""",
            $$"""{{OrdersWire}}, { "from": "position.open", "to": "closed.value" }, { "from": "closed.result", "to": "first.value" }""");
        var run = new GraphRun(History.Graph(json));
        var open = new PositionState(1m, Invested: 100m, AveragePrice: 100m, LastEntryPrice: 100m, LastEntryTime: Monday);
        run.Step(History.Candle(0, 1), facts: null, open);
        Assert.That(run.Step(History.Candle(1, 1), facts: null, open)[0].Happened, Is.False);

        Assert.That(run.Again(PositionState.Closed, History.Candle(1, 1).CloseTime.AddMinutes(7))[0].Happened, Is.True, "closed now, open at the last close");
        Assert.That(run.Again(PositionState.Closed, History.Candle(1, 1).CloseTime.AddMinutes(8))[0].Happened, Is.True, "the memory did not move");
        Assert.That(
            run.Step(History.Candle(2, 1), facts: null, PositionState.Closed)[0].Happened,
            Is.True,
            "the close compares with the close before it, not with the computation again");
        Assert.That(run.Step(History.Candle(3, 1), facts: null, PositionState.Closed)[0].Happened, Is.False);
    }

    // The outputs of the state of the position: open, quantity, averagePrice, lastEntryPrice, invested, sinceLastEntry.
    private static string StateGraph()
    {
        return $$"""
                 {
                   "formatVersion": 1,
                   "outputs": [
                     { "name": "open", "type": "boolean" },
                     { "name": "quantity", "type": "number" },
                     { "name": "average", "type": "number" },
                     { "name": "last", "type": "number" },
                     { "name": "invested", "type": "number" },
                     { "name": "since", "type": "duration" }
                   ],
                   "nodes": [{{Orders}}, { "id": "out", "kind": "outputs" }],
                   "wires": [
                     {{OrdersWire}},
                     { "from": "position.open", "to": "out.open" },
                     { "from": "position.quantity", "to": "out.quantity" },
                     { "from": "position.averagePrice", "to": "out.average" },
                     { "from": "position.lastEntryPrice", "to": "out.last" },
                     { "from": "position.invested", "to": "out.invested" },
                     { "from": "position.sinceLastEntry", "to": "out.since" }
                   ]
                 }
                 """;
    }
}
