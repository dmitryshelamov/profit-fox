using System.Text.Json;

using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.Services.Graph;

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
        return ReadStrategy(json, SignalSet.Empty);
    }

    public static ReadResult<TradingStrategy> ReadStrategy(string json, SignalSet signals)
    {
        return Parse(json, element => ReadStrategy(element, signals));
    }

    /// <summary>
    ///     A strategy: a graph without outputs that enters a position and closes it (spec strategy-graph «Стратегия»). Without
    ///     saved signals, so a <c>signal</c> node is an unknown signal.
    /// </summary>
    public static ReadResult<TradingStrategy> ReadStrategy(JsonElement element)
    {
        return ReadStrategy(element, SignalSet.Empty);
    }

    /// <summary>
    ///     A strategy whose <c>signal</c> nodes point at versions of <paramref name="signals" />, checked as written and then
    ///     expanded (change add-strategy-constructor, design.md, decisions 2 and 3).
    /// </summary>
    public static ReadResult<TradingStrategy> ReadStrategy(JsonElement element, SignalSet signals)
    {
        List<GraphError> errors = [];
        GraphDefinition? graph = GraphJson.Read(element, errors);

        if (graph is null)
        {
            return ReadResult<TradingStrategy>.Invalid(errors);
        }

        errors.AddRange(GraphValidator.ValidateStrategy(graph, signals));

        return errors.Count == 0 ? ReadResult<TradingStrategy>.Valid(Expand(graph, signals)) : ReadResult<TradingStrategy>.Invalid(errors);
    }

    /// <summary>
    ///     The graph as the JSON writes it, with only the errors of its form: what the application reads the references of
    ///     <c>signal</c> nodes from before it checks the meaning.
    /// </summary>
    public static ReadResult<GraphDefinition> ReadForm(JsonElement element)
    {
        List<GraphError> errors = [];
        GraphDefinition? graph = GraphJson.Read(element, errors);

        return graph is null ? ReadResult<GraphDefinition>.Invalid(errors) : ReadResult<GraphDefinition>.Valid(graph);
    }

    // An error of the expanded graph is a defect of the expansion, not of the graph the user wrote.
    private static TradingStrategy Expand(GraphDefinition graph, SignalSet signals)
    {
        GraphDefinition expanded = SignalExpansion.Expand(graph, signals);
        List<GraphError> errors = GraphValidator.ValidateStrategy(expanded);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"The expanded strategy does not pass the checks: {string.Join("; ", errors)}");
        }

        return new TradingStrategy(graph, expanded);
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