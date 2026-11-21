using ProfitFox.Domain.ValueObjects.Execution;

namespace ProfitFox.Tests.Unit.Execution;

/// <summary>
///     Spec strategy-execution: what each node computes, missing values, and that no node looks ahead.
/// </summary>
public sealed class NodeTests
{
    private const string CandleNode = """{ "id": "c", "kind": "candle" }""";

    // close > 0 as a condition: closes of 0 and 1 give false and true.
    private const string Positive = """
                                    { "id": "c", "kind": "candle" },
                                    { "id": "pos", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 0 } }
                                    """;

    private const string PositiveWire = """{ "from": "c.close", "to": "pos.a" }""";

    // The funding rate against the threshold: missing before the first payment.
    private const string HighFunding = """
                                       { "id": "p", "kind": "parameters" },
                                       { "id": "rate", "kind": "funding-rate" },
                                       { "id": "high", "kind": "compare", "settings": { "operation": "ge" } }
                                       """;

    private const string HighFundingWires = """
                                            { "from": "rate.value", "to": "high.a" }, { "from": "p.threshold", "to": "high.b" }
                                            """;

    [Test]
    public void The_candle_node_gives_the_close_of_the_candle()
    {
        List<PinValue[]> outputs = History.Run(History.WithOutput("number", "c.close", CandleNode), History.Closes(101.5m));

        Assert.That(outputs[0][0].Number, Is.EqualTo(101.5m));
    }

    [Test]
    public void A_payment_exactly_at_the_close_is_seen()
    {
        ClosedCandle candle = History.Candle(2, 1);

        List<PinValue[]> outputs = History.Run(
            History.WithOutput("number", "rate.value", """{ "id": "rate", "kind": "funding-rate" }"""),
            [candle],
            _ => new FundingPayment(candle.CloseTime, 0.0006m));

        Assert.That(outputs[0][0].Number, Is.EqualTo(0.0006m));
    }

    [Test]
    public void The_rate_is_missing_before_the_first_payment_and_so_is_the_comparison()
    {
        List<PinValue[]> outputs = History.Run(History.WithOutput("boolean", "high.result", HighFunding, HighFundingWires), History.Closes(1));

        Assert.That(outputs[0][0].IsMissing, Is.True);
    }

    [Test]
    public void Greater_or_equal_holds_on_equal_values()
    {
        ClosedCandle candle = History.Candle(0, 1);

        List<PinValue[]> outputs = History.Run(
            History.WithOutput("boolean", "high.result", HighFunding, HighFundingWires),
            [candle],
            _ => new FundingPayment(candle.CloseTime, 0.0005m));

        Assert.That(outputs[0][0].Boolean, Is.True);
    }

    [TestCase("gt", 1, 2, false)]
    [TestCase("ge", 2, 2, true)]
    [TestCase("lt", 1, 2, true)]
    [TestCase("le", 3, 2, false)]
    [TestCase("eq", 2, 2, true)]
    [TestCase("ne", 2, 2, false)]
    public void Compare_applies_its_operation(string operation, int a, int b, bool expected)
    {
        string json = History.WithOutput(
            "boolean",
            "cmp.result",
            $$"""{ "id": "cmp", "kind": "compare", "settings": { "operation": "{{operation}}" }, "inputs": { "a": {{a}}, "b": {{b}} } }""");

        Assert.That(History.Run(json, History.Closes(1))[0][0].Boolean, Is.EqualTo(expected));
    }

    [Test]
    public void Previous_gives_the_value_of_the_previous_candle()
    {
        string json = History.WithOutput(
            "number",
            "prev.result",
            $$"""{{CandleNode}}, { "id": "prev", "kind": "previous" }""",
            """{ "from": "c.close", "to": "prev.value" }""");

        List<PinValue[]> outputs = History.Run(json, History.Closes(7, 9, 4));

        Assert.That(outputs.Select(output => output[0].Number), Is.EqualTo(new decimal?[] { null, 7, 9 }));
    }

    [Test]
    public void First_time_turns_a_condition_into_events()
    {
        string json = History.WithOutput(
            "event",
            "first.event",
            $$"""{{Positive}}, { "id": "first", "kind": "first-time" }""",
            $$"""{{PositiveWire}}, { "from": "pos.result", "to": "first.value" }""");

        List<PinValue[]> outputs = History.Run(json, History.Closes(0, 1, 1, 0, 1));

        Assert.That(outputs.Select(output => output[0].Happened), Is.EqualTo(new[] { false, true, false, false, true }));
    }

    [Test]
    public void First_time_gives_no_event_after_a_missing_value()
    {
        string json = History.WithOutput(
            "event",
            "first.event",
            $$"""{{HighFunding}}, { "id": "first", "kind": "first-time" }""",
            $$"""{{HighFundingWires}}, { "from": "high.result", "to": "first.value" }""");
        ClosedCandle[] candles = [.. History.Closes(1, 1)];

        List<PinValue[]> outputs = History.Run(
            json,
            candles,
            candle => candle == candles[1] ? new FundingPayment(candle.CloseTime, 0.0007m) : null);

        Assert.That(outputs.Select(output => output[0].Happened), Is.EqualTo(new[] { false, false }));
    }

    [TestCase("and", 0, false)]
    [TestCase("and", 1, null)]
    [TestCase("or", 1, true)]
    [TestCase("or", 0, null)]
    public void And_and_or_with_a_missing_input(string kind, int close, bool? expected)
    {
        string json = History.WithOutput(
            "boolean",
            "both.result",
            $$"""{{Positive}}, {{HighFunding}}, { "id": "both", "kind": "{{kind}}" }""",
            $$"""{{PositiveWire}}, {{HighFundingWires}}, { "from": "pos.result", "to": "both.a" }, { "from": "high.result", "to": "both.b" }""");

        Assert.That(History.Run(json, History.Closes(close))[0][0].Boolean, Is.EqualTo(expected));
    }

    [Test]
    public void Not_keeps_a_missing_value_missing()
    {
        string json = History.WithOutput(
            "boolean",
            "no.result",
            $$"""{{HighFunding}}, { "id": "no", "kind": "not" }""",
            $$"""{{HighFundingWires}}, { "from": "high.result", "to": "no.value" }""");

        Assert.That(History.Run(json, History.Closes(1))[0][0].IsMissing, Is.True);
    }

    [Test]
    public void Parameters_give_their_values()
    {
        string json = History.WithOutput("number", "p.threshold", """{ "id": "p", "kind": "parameters" }""");

        Assert.That(History.Run(json, History.Closes(1))[0][0].Number, Is.EqualTo(0.0005m));
    }

    // Two histories equal up to candle N give the same outputs up to N, whatever comes after (spec: no look-ahead).
    [TestCaseSource(nameof(EveryKind))]
    public void No_node_looks_ahead(string json)
    {
        const int same = 6;
        decimal[] closes = [1, 0, 2, 0, 3, 1, 5, 0, 8, 1];
        decimal[] changed = [.. closes.Take(same), .. closes.Skip(same).Select(close => 100 - close)];

        List<PinValue[]> first = History.Run(json, History.Closes(closes), Payment);
        List<PinValue[]> second = History.Run(json, History.Closes(changed), Payment);

        Assert.That(second.Take(same).SelectMany(output => output), Is.EqualTo(first.Take(same).SelectMany(output => output)));
        Assert.That(second.Skip(same).SelectMany(output => output), Is.Not.EqualTo(first.Skip(same).SelectMany(output => output)));
    }

    private static IEnumerable<string> EveryKind()
    {
        yield return History.WithOutput("number", "c.close", CandleNode);
        yield return History.WithOutput("number", "rate.value", """{ "id": "rate", "kind": "funding-rate" }""");
        yield return History.WithOutput("boolean", "pos.result", Positive, PositiveWire);
        yield return History.WithOutput("number", "prev.result", $$"""{{CandleNode}}, { "id": "prev", "kind": "previous" }""", """{ "from": "c.close", "to": "prev.value" }""");
        yield return History.WithOutput("boolean", "both.result", $$"""{{Positive}}, {{HighFunding}}, { "id": "both", "kind": "and" }""",
            $$"""{{PositiveWire}}, {{HighFundingWires}}, { "from": "pos.result", "to": "both.a" }, { "from": "high.result", "to": "both.b" }""");
        yield return History.WithOutput("boolean", "both.result", $$"""{{Positive}}, {{HighFunding}}, { "id": "both", "kind": "or" }""",
            $$"""{{PositiveWire}}, {{HighFundingWires}}, { "from": "pos.result", "to": "both.a" }, { "from": "high.result", "to": "both.b" }""");
        yield return History.WithOutput("boolean", "no.result", $$"""{{Positive}}, { "id": "no", "kind": "not" }""", $$"""{{PositiveWire}}, { "from": "pos.result", "to": "no.value" }""");
        yield return History.WithOutput("event", "first.event", $$"""{{Positive}}, { "id": "first", "kind": "first-time" }""",
            $$"""{{PositiveWire}}, { "from": "pos.result", "to": "first.value" }""");
    }

    // The payment of each candle follows its close: 0.0001 per unit of close, paid at the close.
    private static FundingPayment Payment(ClosedCandle candle)
    {
        return new FundingPayment(candle.CloseTime, candle.Close * 0.0001m);
    }
}
