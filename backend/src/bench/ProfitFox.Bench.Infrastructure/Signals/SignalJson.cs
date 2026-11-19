using System.Globalization;
using System.Text;
using System.Text.Json;
using ProfitFox.Bench.Domain.Signals;
using ProfitFox.Engine.Strategy.Graph;
using ProfitFox.Engine.Strategy.Graph.Model;
using ProfitFox.Engine.Strategy.Graph.Reading;

namespace ProfitFox.Bench.Infrastructure.Signals;

/// <summary>
///     The JSON of a signal file: <c>name</c>, <c>description</c>, <c>graph</c> and <c>layout</c>. The graph is written by
///     the engine, so moving a node never changes a byte of it.
/// </summary>
internal static class SignalJson
{
    private static readonly string[] Fields = ["name", "description", "graph", "layout"];

    public static string Write(SignalDocument signal)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("name", signal.Name);
            if (signal.Description is { } description)
            {
                writer.WriteString("description", description);
            }

            writer.WritePropertyName("graph");
            StrategyGraph.Write(writer, signal.Graph);
            writer.WriteStartObject("layout");
            foreach (NodeDefinition node in signal.Graph.Nodes)
            {
                if (signal.Layout.TryGetValue(node.Id, out NodePosition? position))
                {
                    writer.WriteStartObject(node.Id);
                    writer.WriteNumber("x", position.X);
                    writer.WriteNumber("y", position.Y);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>
    ///     The signal of a file, or the errors that stop it: of its form, of the graph as a signal, or a name that is not
    ///     the name of the file.
    /// </summary>
    public static (SignalDocument? Signal, IReadOnlyList<string> Errors) Read(string text, string fileName)
    {
        List<string> errors = [];
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            return (null, [$"the file is not JSON: {exception.Message}"]);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, ["the file must be a JSON object"]);
            }

            errors.AddRange(root.EnumerateObject().Where(field => !Fields.Contains(field.Name)).Select(field => $"unknown field {field.Name}"));

            string? name = root.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() : null;
            if (name is null)
            {
                errors.Add("name is required");
            }
            else if (name != fileName)
            {
                errors.Add($"name {name} is not the name of the file {fileName}");
            }

            string? description = null;
            if (root.TryGetProperty("description", out JsonElement descriptionElement))
            {
                if (descriptionElement.ValueKind == JsonValueKind.String)
                {
                    description = descriptionElement.GetString();
                }
                else
                {
                    errors.Add("description must be a string");
                }
            }

            GraphDefinition? graph = null;
            if (root.TryGetProperty("graph", out JsonElement graphElement))
            {
                ReadResult<Signal> read = StrategyGraph.ReadSignal(graphElement);
                graph = read.Value?.Graph;
                errors.AddRange(read.Errors.Select(error => $"graph: {error}"));
            }
            else
            {
                errors.Add("graph is required");
            }

            Dictionary<string, NodePosition> layout = [];
            if (root.TryGetProperty("layout", out JsonElement layoutElement))
            {
                errors.AddRange(ReadLayout(layoutElement, layout));
            }
            else
            {
                errors.Add("layout is required");
            }

            return errors.Count == 0 && name is not null && graph is not null
                ? (new SignalDocument(name, description, graph, layout), [])
                : (null, errors);
        }
    }

    /// <summary>
    ///     The positions of a <c>layout</c> object: each node id maps to <c>{ "x": number, "y": number }</c>.
    /// </summary>
    public static IReadOnlyList<string> ReadLayout(JsonElement element, Dictionary<string, NodePosition> layout)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return ["layout must be an object"];
        }

        List<string> errors = [];
        foreach (JsonProperty node in element.EnumerateObject())
        {
            if (node.Value.ValueKind == JsonValueKind.Object
                && node.Value.TryGetProperty("x", out JsonElement x) && x.TryGetDouble(out double left)
                && node.Value.TryGetProperty("y", out JsonElement y) && y.TryGetDouble(out double top)
                && double.IsFinite(left) && double.IsFinite(top))
            {
                layout[node.Name] = new NodePosition(left, top);
            }
            else
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"layout.{node.Name} must be {{ x, y }} with numbers"));
            }
        }

        return errors;
    }
}
