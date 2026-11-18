using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Tests.Unit.Engine.Strategy.Graph;

[TestFixture]
public class SignalReadingTests
{
    [Test]
    public void The_signal_of_h0001_is_read_without_errors()
    {
        ReadResult<Signal> result = CompositeNodeReader.ReadSignal(Graphs.H0001);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Value!.Output, Is.EqualTo("entry"));
    }

    [Test]
    public void A_composite_node_with_a_number_output_is_not_a_signal()
    {
        string json = Graphs.NumberOutput("""{ "id": "rate", "kind": "funding-rate" }""", wires: "", "rate.value");

        ReadResult<Signal> result = CompositeNodeReader.ReadSignal(json);

        Assert.That(result.Value, Is.Null);
        Assert.That(result.Errors.Select(error => error.Code), Is.EqualTo(new[] { GraphErrorCode.NotASignal }));
    }

    [Test]
    public void A_composite_node_with_two_outputs_is_not_a_signal()
    {
        string json = Graphs.Composite(
            """{ "name": "long", "type": "boolean" }, { "name": "short", "type": "boolean" }""",
            """{ "id": "first", "kind": "and", "inputs": { "a": true, "b": true } }, { "id": "out", "kind": "outputs" }""",
            """{ "from": "first.result", "to": "out.long" }, { "from": "first.result", "to": "out.short" }""");

        ReadResult<Signal> result = CompositeNodeReader.ReadSignal(json);

        Assert.That(result.Value, Is.Null);
        Assert.That(result.Errors.Select(error => error.Code), Is.EqualTo(new[] { GraphErrorCode.NotASignal }));
    }

    [Test]
    public void An_invalid_composite_node_read_as_a_signal_gives_its_errors()
    {
        ReadResult<Signal> result = CompositeNodeReader.ReadSignal("[]");

        Assert.That(result.Value, Is.Null);
        Assert.That(result.Errors.Select(error => error.Code), Is.EqualTo(new[] { GraphErrorCode.InvalidField }));
    }
}