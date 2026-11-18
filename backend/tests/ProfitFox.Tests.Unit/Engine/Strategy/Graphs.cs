using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Tests.Unit.Engine.Strategy;

/// <summary>
///     Definitions of composite nodes for the tests: the signal of H0001 from its fixture and graphs put together from JSON fragments.
/// </summary>
internal static class Graphs
{
    /// <summary>
    ///     The JSON of the signal of H0001: the last funding payment reaches 0.0005 for the first time.
    /// </summary>
    public static string H0001 => File.ReadAllText(Fixture("signal.json"));

    /// <summary>
    ///     The path of a file of the H0001 fixture.
    /// </summary>
    public static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Engine", "Strategy", "Fixtures", "H0001", name);
    }

    /// <summary>
    ///     A definition of version 1 from the JSON of its outputs, nodes and wires, each a comma-separated list of objects.
    /// </summary>
    public static string Composite(string outputs, string nodes, string wires)
    {
        return $$"""{ "formatVersion": 1, "outputs": [{{outputs}}], "nodes": [{{nodes}}], "wires": [{{wires}}] }""";
    }

    /// <summary>
    ///     A graph with one number output <c>value</c> fed by the output <paramref name="pin" /> of the given nodes.
    /// </summary>
    public static string NumberOutput(string nodes, string wires, string pin)
    {
        string allWires = wires.Length == 0 ? $$"""{ "from": "{{pin}}", "to": "out.value" }""" : $$"""{{wires}}, { "from": "{{pin}}", "to": "out.value" }""";
        return Composite(
            """{ "name": "value", "type": "number" }""",
            $$"""{{nodes}}, { "id": "out", "kind": "outputs" }""",
            allWires);
    }

    /// <summary>
    ///     A graph with one boolean output <c>value</c> fed by the output <paramref name="pin" /> of the given nodes.
    /// </summary>
    public static string BooleanOutput(string nodes, string wires, string pin)
    {
        string allWires = wires.Length == 0 ? $$"""{ "from": "{{pin}}", "to": "out.value" }""" : $$"""{{wires}}, { "from": "{{pin}}", "to": "out.value" }""";
        return Composite(
            """{ "name": "value", "type": "boolean" }""",
            $$"""{{nodes}}, { "id": "out", "kind": "outputs" }""",
            allWires);
    }

    /// <summary>
    ///     The checked composite node of a definition that must be valid.
    /// </summary>
    public static CompositeNode Valid(string json)
    {
        ReadResult<CompositeNode> result = CompositeNodeReader.Read(json);
        Assert.That(result.Errors, Is.Empty, "The definition must be valid.");
        return result.Value!;
    }

    /// <summary>
    ///     The errors of a definition that must be invalid.
    /// </summary>
    public static IReadOnlyList<GraphError> Errors(string json)
    {
        ReadResult<CompositeNode> result = CompositeNodeReader.Read(json);
        Assert.That(result.Value, Is.Null, "The definition must be rejected.");
        return result.Errors;
    }
}