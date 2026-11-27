using System.Text.Json;

using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

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
        return ReadStrategy(element, signals, StrategyKind.Bot);
    }

    public static ReadResult<TradingStrategy> ReadStrategy(string json, StrategyKind kind)
    {
        return Parse(json, element => ReadStrategy(element, SignalSet.Empty, kind));
    }

    /// <summary>
    ///     A strategy of <paramref name="kind" />: the checks of a strategy and the rules of the kind (spec strategy-graph
    ///     «Вид стратегии»).
    /// </summary>
    public static ReadResult<TradingStrategy> ReadStrategy(JsonElement element, SignalSet signals, StrategyKind kind)
    {
        List<GraphError> errors = [];
        GraphDefinition? graph = GraphJson.Read(element, errors);

        if (graph is null)
        {
            return ReadResult<TradingStrategy>.Invalid(errors);
        }

        errors.AddRange(GraphValidator.ValidateStrategy(graph, signals, kind));

        return errors.Count == 0 ? ReadResult<TradingStrategy>.Valid(Expand(graph, signals)) : ReadResult<TradingStrategy>.Invalid(errors);
    }

    /// <summary>
    ///     A strategy of <paramref name="kind" /> from a graph already read, without <c>signal</c> nodes: the expanded graph a
    ///     backtest keeps, with the values of a set (change add-strategy-backtest, design.md, decision 3).
    /// </summary>
    public static ReadResult<TradingStrategy> ReadStrategy(GraphDefinition graph, StrategyKind kind)
    {
        List<GraphError> errors = GraphValidator.ValidateStrategy(graph, SignalSet.Empty, kind);
        return errors.Count == 0 ? ReadResult<TradingStrategy>.Valid(Expand(graph, SignalSet.Empty)) : ReadResult<TradingStrategy>.Invalid(errors);
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

    /// <summary>
    ///     The pairs the <c>pair-candle</c> nodes of <paramref name="graph" /> name, each once, in the order of the nodes
    ///     (change add-ideas-and-market, design.md, decision 7). For a strategy with <c>signal</c> nodes give the expanded
    ///     graph: the nodes of its signals name pairs too.
    /// </summary>
    public static IReadOnlyList<string> NamedPairs(GraphDefinition graph)
    {
        List<string> pairs = [];
        foreach (NodeDefinition node in graph.Nodes.Where(node => node.Kind == Nodes.NodeCatalog.PairCandle))
        {
            if (node.Settings.TryGetValue(Nodes.NodeCatalog.Symbol, out string? symbol) && symbol.Length > 0 && !pairs.Contains(symbol))
            {
                pairs.Add(symbol);
            }
        }

        return pairs;
    }

    /// <summary>
    ///     An error for each node <c>pair-candle</c> of <paramref name="graph" /> whose pair is not in
    ///     <paramref name="listed" /> (spec market-history «Неизвестная пара»). A node of an expanded signal is reported on the
    ///     <c>signal</c> node it came from: the user wrote that node.
    /// </summary>
    public static List<GraphError> CheckNamedPairs(GraphDefinition graph, IReadOnlyCollection<string> listed)
    {
        var known = new HashSet<string>(listed, StringComparer.Ordinal);
        List<GraphError> errors = [];
        foreach (NodeDefinition node in graph.Nodes.Where(node => node.Kind == Nodes.NodeCatalog.PairCandle))
        {
            if (!node.Settings.TryGetValue(Nodes.NodeCatalog.Symbol, out string? symbol) || known.Contains(symbol))
            {
                continue;
            }

            int slash = node.Id.IndexOf('/', StringComparison.Ordinal);
            errors.Add(
                slash < 0
                    ? new GraphError(GraphErrorCode.UnknownPair, $"{symbol} нет среди торгуемых бессрочных USDT-пар.") { Node = node.Id, Setting = Nodes.NodeCatalog.Symbol }
                    : new GraphError(GraphErrorCode.UnknownPair, $"Узел {node.Id[(slash + 1)..]} сигнала называет {symbol}, которой нет среди торгуемых бессрочных USDT-пар.")
                    {
                        Node = node.Id[..slash]
                    });
        }

        return errors;
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