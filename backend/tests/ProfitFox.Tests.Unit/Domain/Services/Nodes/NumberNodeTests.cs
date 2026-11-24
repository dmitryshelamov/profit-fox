using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Tests.Unit.Domain.Services.Nodes;

/// <summary>
///     Change add-setup-strategies, tasks 2.1–2.3: <c>divide</c>, <c>min</c>, <c>max</c>, <c>select</c> and the input
///     <c>length</c> of the window nodes (spec strategy-execution «Деление», «Меньшее и большее», «Выбор значения», «Длина
///     окна»).
/// </summary>
public sealed class NumberNodeTests
{
    private static readonly string[] WindowKinds = ["window", "percentile", "hold", "atr", "rsi", "zones"];

    [Test]
    public void Divide_gives_a_fraction_of_the_price()
    {
        Assert.That(Two("divide", "3.5", "100"), Is.EqualTo(0.035m));
    }

    [Test]
    public void Divide_by_zero_is_missing_and_the_graph_goes_on()
    {
        string json = NodeGraphs.Graph(
            "number",
            "sum.result",
            """
            { "id": "d", "kind": "divide", "inputs": { "a": 1, "b": 0 } },
            { "id": "sum", "kind": "add", "inputs": { "b": 2 } },
            { "id": "c", "kind": "candle" }
            """,
            """{ "from": "c.close", "to": "sum.a" }""");

        Assert.That(NodeGraphs.Numbers(json, NodeGraphs.Closes(5)), Is.EqualTo(new decimal?[] { 7 }));
        Assert.That(Two("divide", "1", "0"), Is.Null);
    }

    [Test]
    public void Divide_past_the_range_of_a_decimal_is_missing()
    {
        Assert.That(Two("divide", "79228162514264337593543950335", "0.5"), Is.Null);
    }

    [Test]
    public void Divide_with_a_missing_input_is_missing()
    {
        Assert.That(Wired("divide"), Is.Null);
    }

    [Test]
    public void Max_of_the_two_signs_of_a_body()
    {
        Assert.That(Two("max", "-1.2", "1.2"), Is.EqualTo(1.2m));
    }

    [Test]
    public void Min_gives_the_bottom_of_a_body()
    {
        Assert.That(Two("min", "100.6", "100.4"), Is.EqualTo(100.4m));
    }

    [TestCase("min")]
    [TestCase("max")]
    public void Min_and_max_with_a_missing_input_are_missing(string kind)
    {
        Assert.That(Wired(kind), Is.Null);
    }

    [Test]
    public void Select_takes_a_when_true_even_with_b_missing()
    {
        Assert.That(Select("true", a: "0.035", bMissing: true), Is.EqualTo(0.035m));
    }

    [Test]
    public void Select_takes_b_when_false()
    {
        Assert.That(Select("false", a: "0.035", b: "0.045"), Is.EqualTo(0.045m));
    }

    [Test]
    public void Select_takes_b_when_false_even_with_a_missing()
    {
        Assert.That(Select("false", aMissing: true, b: "0.045"), Is.EqualTo(0.045m));
    }

    [Test]
    public void Select_with_an_unknown_condition_is_missing()
    {
        Assert.That(Select(when: null, a: "0.035", b: "0.045"), Is.Null);
    }

    [TestCaseSource(nameof(WindowKinds))]
    public void A_length_written_outside_one_to_a_thousand_is_an_error_on_the_input(string kind)
    {
        foreach (string length in new[] { "0", "1001", "2.5", "-3" })
        {
            GraphError error = Errors(Lengthed(kind, length)).Single();

            Assert.That((error.Code, error.Node, error.Pin), Is.EqualTo((GraphErrorCode.ParameterOutOfBounds, "n", "length")), $"{kind} {length}");
        }

        Assert.That(Errors(Lengthed(kind, "1")), Is.Empty);
        Assert.That(Errors(Lengthed(kind, "1000")), Is.Empty);
    }

    [Test]
    public void The_length_of_atr_written_as_zero_is_an_error()
    {
        GraphError error = Errors(Lengthed("atr", "0")).Single();

        Assert.That(error.Pin, Is.EqualTo("length"));
        Assert.That(error.Reason, Does.Contain("1").And.Contain("1000"));
    }

    [TestCaseSource(nameof(WindowKinds))]
    public void A_length_from_a_parameter_outside_the_bounds_gives_no_value_at_any_candle(string kind)
    {
        foreach (string length in new[] { "2.5", "0", "1001" })
        {
            string json = Lengthed(kind, length: null, $$"""{ "name": "len", "type": "number", "value": {{length}} }""");
            decimal?[] values = NodeGraphs.Numbers(json, Rising(30));

            Assert.That(values, Has.All.Null, $"{kind} {length}");
        }
    }

    [TestCaseSource(nameof(WindowKinds))]
    public void A_length_from_a_parameter_inside_the_bounds_gives_values(string kind)
    {
        string json = Lengthed(kind, length: null, """{ "name": "len", "type": "number", "value": 10 }""");

        Assert.That(NodeGraphs.Numbers(json, Rising(30)).Last(), Is.Not.Null, kind);
    }

    private static decimal? Two(string kind, string a, string b)
    {
        string json = NodeGraphs.Graph("number", "n.result", $$"""{ "id": "n", "kind": "{{kind}}", "inputs": { "a": {{a}}, "b": {{b}} } }""");
        return NodeGraphs.Numbers(json, NodeGraphs.Closes(1))[0];
    }

    // b comes from previous, which has no value at the first candle.
    private static decimal? Wired(string kind)
    {
        string json = NodeGraphs.Graph(
            "number",
            "n.result",
            $$"""{ "id": "c", "kind": "candle" }, { "id": "p", "kind": "previous" }, { "id": "n", "kind": "{{kind}}", "inputs": { "a": 1 } }""",
            """{ "from": "c.close", "to": "p.value" }, { "from": "p.result", "to": "n.b" }""");
        return NodeGraphs.Numbers(json, NodeGraphs.Closes(1))[0];
    }

    // A missing input is the result of previous at the first candle; an unknown condition is close > previous close there.
    private static decimal? Select(string? when, string? a = null, string? b = null, bool aMissing = false, bool bMissing = false)
    {
        List<string> inputs = [];
        List<string> wires = [];
        if (when is null)
        {
            wires.Add("""{ "from": "gt.result", "to": "n.when" }""");
        }
        else
        {
            inputs.Add($"\"when\": {when}");
        }

        foreach ((string pin, string? value, bool missing) in new[] { ("a", a, aMissing), ("b", b, bMissing) })
        {
            if (missing)
            {
                wires.Add($$"""{ "from": "p.result", "to": "n.{{pin}}" }""");
            }
            else
            {
                inputs.Add($"\"{pin}\": {value}");
            }
        }

        string json = NodeGraphs.Graph(
            "number",
            "n.result",
            $$"""
              { "id": "c", "kind": "candle" }, { "id": "p", "kind": "previous" },
              { "id": "gt", "kind": "compare", "settings": { "operation": "gt" } },
              { "id": "n", "kind": "select", "inputs": { {{string.Join(", ", inputs)}} } }
              """,
            string.Join(", ", wires.Prepend("""{ "from": "c.close", "to": "gt.a" }, { "from": "p.result", "to": "gt.b" }, { "from": "c.close", "to": "p.value" }""")));
        return NodeGraphs.Numbers(json, NodeGraphs.Closes(1))[0];
    }

    /// <summary>
    ///     A graph with one node of <paramref name="kind" /> whose length is written, or comes from the parameter
    ///     <c>len</c> when <paramref name="length" /> is null; its other inputs are the candle and fixed values.
    /// </summary>
    internal static string Lengthed(string kind, string? length, string parameters = "")
    {
        string lengthInput = length is null ? "" : $"\"length\": {length}";
        string lengthWire = length is null ? """, { "from": "p.len", "to": "n.length" }""" : "";
        (string inputs, string wires, string from) = kind switch
        {
            "window" => ("", """{ "from": "c.close", "to": "n.value" }""", "n.result"),
            "percentile" => ("\"rank\": 0.5", """{ "from": "c.close", "to": "n.value" }""", "n.result"),
            "hold" => ("\"when\": true", """{ "from": "c.close", "to": "n.value" }""", "n.result"),
            "atr" => ("", "", "n.result"),
            "rsi" => ("", """{ "from": "c.close", "to": "n.value" }""", "n.result"),
            "zones" => ("\"atr\": 1, \"merge\": 0.5, \"strength\": 1", """{ "from": "c.close", "to": "n.price" }""", "n.below"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
        string all = string.Join(", ", new[] { inputs, lengthInput }.Where(part => part.Length > 0));
        string allWires = wires + lengthWire;
        if (allWires.StartsWith(", ", StringComparison.Ordinal))
        {
            allWires = allWires[2..];
        }

        string parametersNode = length is null ? """{ "id": "p", "kind": "parameters" }, """ : "";
        string settings = kind == "window" ? """, "settings": { "operation": "max" }""" : "";
        return NodeGraphs.Graph(
            "number",
            from,
            $$"""{{parametersNode}}{ "id": "c", "kind": "candle" }, { "id": "n", "kind": "{{kind}}"{{settings}}, "inputs": { {{all}} } }""",
            allWires,
            parameters);
    }

    // Candles that go up and down by turns, with a top every fourth candle, so that every window kind has a value.
    private static IEnumerable<ProfitFox.Domain.ValueObjects.Execution.ClosedCandle> Rising(int count)
    {
        return Enumerable.Range(0, count).Select(index =>
        {
            decimal close = 100 + index + (index % 4 == 2 ? 3 : 0) - (index % 4 == 0 ? 2 : 0);
            return NodeGraphs.Bar(index, close - 0.5m, close + 1, close - 1, close);
        });
    }

    private static List<GraphError> Errors(string json)
    {
        ReadResult<GraphDefinition> result = StrategyGraph.Read(json);
        return [.. result.Errors];
    }
}
