using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Tests.Unit.Domain.Services.Execution;

namespace ProfitFox.Tests.Unit.Domain.Services.Nodes;

/// <summary>
///     Change add-setup-strategies, task 8.1: <c>is-true</c> (spec strategy-execution «Известная истина»; design.md, decision
///     11) — true when its input is true, false when it is false or missing, and never missing itself.
/// </summary>
public sealed class IsTrueNodeTests
{
    // close > previous close: missing at the first candle, then known. The closes 1, 2, 1 give missing, true, false.
    private const string Rising = """
                                  { "id": "c", "kind": "candle" }, { "id": "prev", "kind": "previous" },
                                  { "id": "up", "kind": "compare", "settings": { "operation": "gt" } },
                                  { "id": "known", "kind": "is-true" }
                                  """;

    private const string RisingWires = """
                                       { "from": "c.close", "to": "prev.value" }, { "from": "c.close", "to": "up.a" },
                                       { "from": "prev.result", "to": "up.b" }, { "from": "up.result", "to": "known.value" }
                                       """;

    // «Истина остаётся истиной».
    [Test]
    public void A_true_input_stays_true()
    {
        Assert.That(Results(History.Closes(1, 2))[1], Is.True);
    }

    // «Неизвестное становится ложью»: the comparison has no previous close to read at the first candle.
    [Test]
    public void A_missing_input_becomes_false()
    {
        List<PinValue[]> outputs = History.Run(History.WithOutput("boolean", "known.result", Rising, RisingWires), History.Closes(1));

        Assert.That(outputs[0][0].IsMissing, Is.False);
        Assert.That(outputs[0][0].Boolean, Is.False);
    }

    [Test]
    public void A_false_input_stays_false()
    {
        Assert.That(Results(History.Closes(1, 2, 1))[2], Is.False);
    }

    [Test]
    public void A_written_input_is_read_as_it_is()
    {
        string json = History.WithOutput("boolean", "known.result", """{ "id": "known", "kind": "is-true", "inputs": { "value": true } }""");

        Assert.That(History.Run(json, History.Closes(1))[0][0].Boolean, Is.True);
    }

    // «Событие после свечи без значения»: first-time on is-true gives an event where first-time on the comparison gives none.
    [Test]
    public void First_time_on_is_true_gives_an_event_after_a_candle_without_a_value()
    {
        string known = History.WithOutput(
            "event",
            "first.event",
            $$"""{{Rising}}, { "id": "first", "kind": "first-time" }""",
            $$"""{{RisingWires}}, { "from": "known.result", "to": "first.value" }""");
        string plain = History.WithOutput(
            "event",
            "first.event",
            $$"""{{Rising}}, { "id": "first", "kind": "first-time" }""",
            $$"""{{RisingWires}}, { "from": "up.result", "to": "first.value" }""");

        Assert.That(History.Run(known, History.Closes(1, 2)).Select(output => output[0].Happened), Is.EqualTo(new[] { false, true }));
        Assert.That(History.Run(plain, History.Closes(1, 2)).Select(output => output[0].Happened), Is.EqualTo(new[] { false, false }));
    }

    // The node keeps nothing: a computation again after a fill gives what the close gave.
    [Test]
    public void A_computation_again_after_a_fill_gives_the_same_answer()
    {
        GraphRun run = NodeGraphs.Run(History.WithOutput("boolean", "known.result", Rising, RisingWires));

        foreach (ClosedCandle candle in History.Closes(1, 2, 1, 1, 3))
        {
            bool? atClose = run.Step(candle, lastFunding: null)[0].Boolean;
            bool? afterFill = run.Again(PositionState.Closed, candle.CloseTime.AddMinutes(1))[0].Boolean;

            Assert.That(afterFill, Is.EqualTo(atClose), candle.OpenTime.ToString("O"));
        }
    }

    private static bool?[] Results(IEnumerable<ClosedCandle> candles)
    {
        return [.. History.Run(History.WithOutput("boolean", "known.result", Rising, RisingWires), candles).Select(output => output[0].Boolean)];
    }
}
