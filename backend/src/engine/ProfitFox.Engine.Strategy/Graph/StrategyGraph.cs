using System.Text.Json;

using ProfitFox.Engine.Strategy.Graph.Model;
using ProfitFox.Engine.Strategy.Graph.Reading;

namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     Reading and writing graphs. Engine does not read files: the caller gives the JSON text or element.
/// </summary>
public static class StrategyGraph
{
    public static ReadResult<GraphDefinition> Read(string json)
    {
        return Parse(json, Read);
    }

    /// <summary>
    ///     The graph with every error: of its form, or, when the form is right, of its meaning.
    /// </summary>
    public static ReadResult<GraphDefinition> Read(JsonElement element)
    {
        List<GraphError> errors = [];
        GraphDefinition? graph = GraphJson.Read(element, errors);

        if (graph is null)
        {
            return ReadResult<GraphDefinition>.Invalid(errors);
        }

        errors.AddRange(GraphValidator.Validate(graph));

        return errors.Count == 0 ? ReadResult<GraphDefinition>.Valid(graph) : ReadResult<GraphDefinition>.Invalid(errors);
    }

    public static ReadResult<Signal> ReadSignal(string json)
    {
        return Parse(json, ReadSignal);
    }

    /// <summary>
    ///     A signal: a graph with exactly one output, an event (spec strategy-graph).
    /// </summary>
    public static ReadResult<Signal> ReadSignal(JsonElement element)
    {
        List<GraphError> errors = [];
        GraphDefinition? graph = GraphJson.Read(element, errors);

        if (graph is null)
        {
            return ReadResult<Signal>.Invalid(errors);
        }

        errors.AddRange(GraphValidator.ValidateSignal(graph));

        return errors.Count == 0 ? ReadResult<Signal>.Valid(new Signal(graph)) : ReadResult<Signal>.Invalid(errors);
    }

    public static ReadResult<TradingStrategy> ReadStrategy(string json)
    {
        return Parse(json, ReadStrategy);
    }

    /// <summary>
    ///     A strategy: a graph without outputs that enters a position and closes it (spec strategy-graph «Стратегия»).
    /// </summary>
    public static ReadResult<TradingStrategy> ReadStrategy(JsonElement element)
    {
        List<GraphError> errors = [];
        GraphDefinition? graph = GraphJson.Read(element, errors);

        if (graph is null)
        {
            return ReadResult<TradingStrategy>.Invalid(errors);
        }

        errors.AddRange(GraphValidator.ValidateStrategy(graph));

        return errors.Count == 0 ? ReadResult<TradingStrategy>.Valid(new TradingStrategy(graph)) : ReadResult<TradingStrategy>.Invalid(errors);
    }

    public static string Write(GraphDefinition graph)
    {
        return GraphJson.Write(graph);
    }

    public static void Write(Utf8JsonWriter writer, GraphDefinition graph)
    {
        GraphJson.Write(writer, graph);
    }

    private static ReadResult<T> Parse<T>(string json, Func<JsonElement, ReadResult<T>> read)
        where T : class
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return ReadResult<T>.Invalid([new GraphError(GraphErrorCode.NotJson, $"The text is not JSON: {exception.Message}")]);
        }

        using (document)
        {
            return read(document.RootElement);
        }
    }
}