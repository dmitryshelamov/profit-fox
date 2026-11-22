using System.Text.Json;

using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.Services.Graph;

/// <summary>
///     Other values of the parameters of a graph (change add-strategy-backtest, design.md, decision 3): a set of a backtest
///     runs the graph of the version with its own values. A value comes as text and is read as the JSON of a graph writes
///     a value — a number, <c>true</c> or <c>false</c>, or a duration such as <c>4h</c>; the checks of the parameters then
///     tell a value of another type or outside the bounds with the errors they always gave.
/// </summary>
public static class GraphParameters
{
    /// <summary>
    ///     A copy of <paramref name="graph" /> whose named parameters have the new values; the graph itself does not change,
    ///     and without values it is returned as it is. A name the graph has no parameter of, or a text that is no value at
    ///     all, is added to <paramref name="errors" /> and leaves the parameter as it was.
    /// </summary>
    public static GraphDefinition With(GraphDefinition graph, IReadOnlyList<KeyValuePair<string, string>> values, List<GraphError> errors)
    {
        if (values.Count == 0)
        {
            return graph;
        }

        var parameters = graph.Parameters.ToList();
        foreach ((string name, string text) in values)
        {
            int index = parameters.FindIndex(parameter => parameter.Name == name);
            if (index < 0)
            {
                errors.Add(new GraphError(GraphErrorCode.UnknownParameter, $"The strategy has no parameter {name}.") { Parameter = name });
                continue;
            }

            ParameterDefinition parameter = parameters[index];
            if (!TryRead(text, out GraphValue value))
            {
                errors.Add(
                    new GraphError(GraphErrorCode.ParameterTypeMismatch, $"{text} is not a value of the parameter: it is {PinTypes.Name(parameter.Type)}.")
                    {
                        Parameter = name
                    });
                continue;
            }

            parameters[index] = parameter with { Value = value };
        }

        return graph with { Parameters = parameters };
    }

    /// <summary>
    ///     The errors of the parameters of <paramref name="graph" /> alone: types, bounds and the values within them, as the
    ///     checks of a graph give them.
    /// </summary>
    public static List<GraphError> Check(GraphDefinition graph)
    {
        List<GraphError> errors = [];
        GraphValidator.CheckParameters(graph, errors);
        return errors;
    }

    /// <summary>
    ///     The value a text is: a JSON number or <c>true</c> or <c>false</c>, or else the text as a duration.
    /// </summary>
    public static bool TryRead(string text, out GraphValue value)
    {
        JsonElement element;
        try
        {
            using var document = JsonDocument.Parse(text);
            element = document.RootElement.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                ? document.RootElement.Clone()
                : JsonSerializer.SerializeToElement(text);
        }
        catch (JsonException)
        {
            element = JsonSerializer.SerializeToElement(text);
        }

        return GraphJson.TryReadValue(element, out value);
    }
}
