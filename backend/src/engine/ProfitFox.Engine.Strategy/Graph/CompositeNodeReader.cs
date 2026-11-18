namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     Reads a composite node or a signal from its JSON text and checks it. Engine reads no files: the caller gives the text.
/// </summary>
public static class CompositeNodeReader
{
    /// <summary>
    ///     The checked composite node, or every error found in the text.
    /// </summary>
    public static ReadResult<CompositeNode> Read(string json)
    {
        List<GraphError> errors = [];
        CompositeNode? node = ReadNode(json, errors, out _);
        return node is null ? ReadResult<CompositeNode>.Invalid(errors) : ReadResult<CompositeNode>.Valid(node);
    }

    /// <summary>
    ///     The checked signal — a composite node with exactly one logical output — or every error found in the text.
    /// </summary>
    public static ReadResult<Signal> ReadSignal(string json)
    {
        List<GraphError> errors = [];
        CompositeNode? node = ReadNode(json, errors, out CompositeNodeDefinition? definition);
        if (definition is not null && definition.Outputs is not [{ Type: PinType.Boolean }])
        {
            errors.Add(new GraphError(GraphErrorCode.NotASignal, "A signal has exactly one output, of the boolean type."));
        }

        return errors.Count == 0 ? ReadResult<Signal>.Valid(new Signal(node!)) : ReadResult<Signal>.Invalid(errors);
    }

    private static CompositeNode? ReadNode(string json, List<GraphError> errors, out CompositeNodeDefinition? definition)
    {
        definition = DefinitionParser.Parse(json, errors);
        return definition is null ? null : GraphValidator.Validate(definition, errors);
    }
}