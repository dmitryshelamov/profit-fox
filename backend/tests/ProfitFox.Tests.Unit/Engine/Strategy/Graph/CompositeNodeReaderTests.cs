using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Tests.Unit.Engine.Strategy.Graph;

[TestFixture]
public class CompositeNodeReaderTests
{
    private const string Rate = """{ "id": "rate", "kind": "funding-rate" }""";

    [Test]
    public void A_definition_of_another_format_version_is_rejected()
    {
        string json = Graphs.H0001.Replace("\"formatVersion\": 1", "\"formatVersion\": 2", StringComparison.Ordinal);

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Field)), Is.EqualTo(new[] { (GraphErrorCode.UnsupportedFormatVersion, "formatVersion") }));
    }

    [Test]
    public void A_definition_without_a_format_version_is_rejected()
    {
        string json = Graphs.H0001.Replace("\"formatVersion\": 1,", "", StringComparison.Ordinal);

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => error.Code), Is.EqualTo(new[] { GraphErrorCode.UnsupportedFormatVersion }));
    }

    [Test]
    public void An_unknown_field_of_a_node_is_an_error_with_the_node_and_the_field()
    {
        string json = Graphs.BooleanOutput(
            """{ "id": "now", "kind": "compare", "settings": { "operation": "ge" }, "inputs": { "a": 1, "b": 2 }, "layout": { "x": 10 } }""",
            wires: "",
            "now.result");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(errors[0].Code, Is.EqualTo(GraphErrorCode.UnknownField));
            Assert.That(errors[0].Node, Is.EqualTo("now"));
            Assert.That(errors[0].Field, Is.EqualTo("nodes[0].layout"));
        });
    }

    [Test]
    public void An_unknown_field_of_the_definition_is_an_error()
    {
        string json = Graphs.H0001.Replace("\"formatVersion\": 1,", "\"formatVersion\": 1, \"layout\": {},", StringComparison.Ordinal);

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Field)), Is.EqualTo(new[] { (GraphErrorCode.UnknownField, "layout") }));
    }

    [Test]
    public void A_text_that_is_not_json_is_rejected()
    {
        IReadOnlyList<GraphError> errors = Graphs.Errors("{ formatVersion: 1");

        Assert.That(errors.Select(error => error.Code), Is.EqualTo(new[] { GraphErrorCode.NotJson }));
    }

    [Test]
    public void A_missing_array_of_the_definition_is_an_error()
    {
        IReadOnlyList<GraphError> errors = Graphs.Errors("""{ "formatVersion": 1, "outputs": [], "nodes": [] }""");

        Assert.That(errors.Select(error => (error.Code, error.Field)), Is.EqualTo(new[] { (GraphErrorCode.MissingField, "wires") }));
    }

    [Test]
    public void A_wire_end_not_of_the_form_node_pin_is_an_error()
    {
        string json = Graphs.NumberOutput(Rate, """{ "from": "rate", "to": "out.value" }""", "rate.value");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Field)), Is.EqualTo(new[] { (GraphErrorCode.InvalidField, "wires[0].from") }));
    }

    [Test]
    public void Two_nodes_with_one_id_are_an_error()
    {
        string json = Graphs.NumberOutput($"{Rate}, {Rate}", wires: "", "rate.value");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Node)), Is.EqualTo(new[] { (GraphErrorCode.DuplicateNodeId, "rate") }));
    }

    [Test]
    public void A_node_of_an_unknown_kind_is_an_error_with_its_id()
    {
        string json = Graphs.NumberOutput("""{ "id": "fast", "kind": "ema" }""", wires: "", "fast.value");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Node)), Is.EqualTo(new[] { (GraphErrorCode.UnknownKind, "fast") }));
    }

    [TestCase("a.b")]
    [TestCase("1st")]
    [TestCase("rate_1")]
    public void A_node_id_that_is_not_a_latin_letter_followed_by_letters_digits_or_dashes_is_an_error(string id)
    {
        string json = Graphs.Composite(
            """{ "name": "value", "type": "number" }""",
            $$"""{ "id": "{{id}}", "kind": "funding-rate" }, { "id": "out", "kind": "outputs" }""",
            wires: "");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => error.Code), Does.Contain(GraphErrorCode.InvalidName));
        Assert.That(errors.First(error => error.Code == GraphErrorCode.InvalidName).Node, Is.EqualTo(id));
    }

    [Test]
    public void A_compare_without_an_operation_is_an_error_of_the_setting()
    {
        string json = Graphs.BooleanOutput("""{ "id": "now", "kind": "compare", "inputs": { "a": 1, "b": 2 } }""", wires: "", "now.result");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(
            errors.Select(error => (error.Code, error.Node, error.Setting)),
            Is.EqualTo(new[] { (GraphErrorCode.MissingSetting, "now", "operation") }));
    }

    [Test]
    public void An_operation_outside_its_values_is_an_error_that_lists_them()
    {
        string json = Graphs.BooleanOutput(
            """{ "id": "now", "kind": "compare", "settings": { "operation": "between" }, "inputs": { "a": 1, "b": 2 } }""",
            wires: "",
            "now.result");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(errors[0].Code, Is.EqualTo(GraphErrorCode.InvalidSettingValue));
            Assert.That(errors[0].Setting, Is.EqualTo("operation"));
            Assert.That(errors[0].Reason, Does.Contain("ge, lt"));
        });
    }

    [Test]
    public void A_setting_the_kind_does_not_declare_is_an_error()
    {
        string json = Graphs.NumberOutput("""{ "id": "rate", "kind": "funding-rate", "settings": { "period": "8h" } }""", wires: "", "rate.value");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(
            errors.Select(error => (error.Code, error.Node, error.Setting)),
            Is.EqualTo(new[] { (GraphErrorCode.UnknownSetting, "rate", "period") }));
    }

    [Test]
    public void A_number_on_an_input_is_read_without_loss_of_precision()
    {
        string json = Graphs.BooleanOutput(
            """{ "id": "now", "kind": "compare", "settings": { "operation": "ge" }, "inputs": { "a": 1, "b": 0.1234567890123456789012345678 } }""",
            wires: "",
            "now.result");
        CompositeNode graph = Graphs.Valid(json);

        PinValue threshold = graph.Nodes.Single(node => node.Id == "now").Inputs["b"];

        Assert.That(threshold.Number, Is.EqualTo(0.1234567890123456789012345678m));
    }

    [Test]
    public void The_threshold_of_h0001_is_exactly_five_ten_thousandths()
    {
        CompositeNode graph = Graphs.Valid(Graphs.H0001);

        Assert.That(graph.Nodes.Single(node => node.Id == "now").Inputs["b"].Number, Is.EqualTo(0.0005m));
    }

    [Test]
    public void A_wire_from_a_boolean_output_to_a_number_input_is_an_error_naming_both_pins()
    {
        string json = Graphs.BooleanOutput(
            """
            { "id": "first", "kind": "and", "inputs": { "a": true, "b": true } },
            { "id": "now", "kind": "compare", "settings": { "operation": "ge" }, "inputs": { "b": 1 } }
            """,
            """{ "from": "first.result", "to": "now.a" }""",
            "now.result");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Node, error.Pin)), Is.EqualTo(new[] { (GraphErrorCode.TypeMismatch, "now", "a") }));
        Assert.That(errors[0].Reason, Does.Contain("first.result").And.Contain("now.a"));
    }

    [Test]
    public void A_second_wire_to_an_input_is_an_error_of_that_input()
    {
        string json = Graphs.BooleanOutput(
            """
            { "id": "x", "kind": "compare", "settings": { "operation": "ge" }, "inputs": { "a": 1, "b": 2 } },
            { "id": "y", "kind": "compare", "settings": { "operation": "lt" }, "inputs": { "a": 1, "b": 2 } },
            { "id": "first", "kind": "and", "inputs": { "b": true } }
            """,
            """{ "from": "x.result", "to": "first.a" }, { "from": "y.result", "to": "first.a" }""",
            "first.result");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Node, error.Pin)), Is.EqualTo(new[] { (GraphErrorCode.SecondWire, "first", "a") }));
    }

    [Test]
    public void A_wire_to_a_pin_the_node_does_not_have_is_an_error()
    {
        string json = Graphs.BooleanOutput(
            $$"""{{Rate}}, { "id": "now", "kind": "compare", "settings": { "operation": "ge" }, "inputs": { "a": 1, "b": 2 } }""",
            """{ "from": "rate.value", "to": "now.c" }""",
            "now.result");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Node, error.Pin)), Is.EqualTo(new[] { (GraphErrorCode.UnknownPin, "now", "c") }));
    }

    [Test]
    public void A_wire_to_a_node_the_graph_does_not_have_is_an_error()
    {
        string json = Graphs.NumberOutput(Rate, """{ "from": "rate.value", "to": "prev.value" }""", "rate.value");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Node)), Is.EqualTo(new[] { (GraphErrorCode.UnknownNode, "prev") }));
    }

    [Test]
    public void An_input_without_a_wire_takes_its_value()
    {
        string json = Graphs.BooleanOutput(
            $$"""{{Rate}}, { "id": "now", "kind": "compare", "settings": { "operation": "ge" }, "inputs": { "b": 0.0005 } }""",
            """{ "from": "rate.value", "to": "now.a" }""",
            "now.result");

        CompositeNode graph = Graphs.Valid(json);

        Assert.That(graph.Nodes.Single(node => node.Id == "now").Inputs["b"], Is.EqualTo(PinValue.FromNumber(0.0005m)));
    }

    [Test]
    public void An_input_with_neither_a_wire_nor_a_value_is_an_error()
    {
        string json = Graphs.BooleanOutput(
            $$"""{{Rate}}, { "id": "now", "kind": "compare", "settings": { "operation": "ge" } }""",
            """{ "from": "rate.value", "to": "now.a" }""",
            "now.result");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Node, error.Pin)), Is.EqualTo(new[] { (GraphErrorCode.MissingInputValue, "now", "b") }));
    }

    [Test]
    public void An_input_with_both_a_wire_and_a_value_is_an_error()
    {
        string json = Graphs.BooleanOutput(
            $$"""{{Rate}}, { "id": "now", "kind": "compare", "settings": { "operation": "ge" }, "inputs": { "a": 1, "b": 2 } }""",
            """{ "from": "rate.value", "to": "now.a" }""",
            "now.result");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Node, error.Pin)), Is.EqualTo(new[] { (GraphErrorCode.ValueOnWiredInput, "now", "a") }));
    }

    [Test]
    public void A_value_of_another_type_than_its_input_is_an_error()
    {
        string json = Graphs.BooleanOutput("""{ "id": "now", "kind": "compare", "settings": { "operation": "ge" }, "inputs": { "a": true, "b": 2 } }""", wires: "", "now.result");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Node, error.Pin)), Is.EqualTo(new[] { (GraphErrorCode.TypeMismatch, "now", "a") }));
    }

    [Test]
    public void A_value_of_an_input_the_node_does_not_have_is_an_error()
    {
        string json = Graphs.NumberOutput("""{ "id": "rate", "kind": "funding-rate", "inputs": { "a": 1 } }""", wires: "", "rate.value");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Node, error.Pin)), Is.EqualTo(new[] { (GraphErrorCode.UnknownPin, "rate", "a") }));
    }

    [Test]
    public void Wires_that_make_a_cycle_are_an_error_listing_its_nodes()
    {
        string json = Graphs.NumberOutput(
            """{ "id": "x", "kind": "previous" }, { "id": "y", "kind": "previous" }""",
            """{ "from": "x.result", "to": "y.value" }, { "from": "y.result", "to": "x.value" }""",
            "x.result");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => error.Code), Is.EqualTo(new[] { GraphErrorCode.Cycle }));
        Assert.That(errors[0].Reason, Does.Contain("x → y → x"));
    }

    [Test]
    public void A_declared_output_without_a_wire_is_an_error()
    {
        string json = Graphs.Composite(
            """{ "name": "entry", "type": "boolean" }""",
            """{ "id": "out", "kind": "outputs" }""",
            wires: "");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Node, error.Pin)), Is.EqualTo(new[] { (GraphErrorCode.UnwiredOutput, "out", "entry") }));
    }

    [Test]
    public void A_composite_node_without_a_node_of_outputs_is_an_error()
    {
        string json = Graphs.Composite("""{ "name": "value", "type": "number" }""", Rate, wires: "");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => error.Code), Is.EqualTo(new[] { GraphErrorCode.OutputsNodeCount }));
    }

    [Test]
    public void Two_outputs_with_one_name_are_an_error()
    {
        string json = Graphs.Composite(
            """{ "name": "value", "type": "number" }, { "name": "value", "type": "number" }""",
            $$"""{{Rate}}, { "id": "out", "kind": "outputs" }""",
            """{ "from": "rate.value", "to": "out.value" }""");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(errors.Select(error => (error.Code, error.Pin)), Is.EqualTo(new[] { (GraphErrorCode.DuplicateOutputName, "value") }));
    }

    [Test]
    public void Every_error_is_reported_at_once()
    {
        string json = Graphs.BooleanOutput(
            """
            { "id": "first", "kind": "and", "inputs": { "a": true, "b": true } },
            { "id": "now", "kind": "compare", "settings": { "operation": "ge" } }
            """,
            """{ "from": "first.result", "to": "now.a" }""",
            "now.result");

        IReadOnlyList<GraphError> errors = Graphs.Errors(json);

        Assert.That(
            errors.Select(error => (error.Code, error.Node, error.Pin)),
            Is.EquivalentTo(new[] { (GraphErrorCode.TypeMismatch, "now", "a"), (GraphErrorCode.MissingInputValue, "now", "b") }));
    }

    [Test]
    public void The_nodes_come_after_the_nodes_that_feed_them()
    {
        string json = Graphs.NumberOutput(
            """{ "id": "y", "kind": "previous" }, { "id": "x", "kind": "previous" }, { "id": "rate", "kind": "funding-rate" }""",
            """{ "from": "x.result", "to": "y.value" }, { "from": "rate.value", "to": "x.value" }""",
            "y.result");

        CompositeNode graph = Graphs.Valid(json);

        Assert.That(graph.Nodes.Select(node => node.Id), Is.EqualTo(new[] { "rate", "x", "y", "out" }));
    }
}