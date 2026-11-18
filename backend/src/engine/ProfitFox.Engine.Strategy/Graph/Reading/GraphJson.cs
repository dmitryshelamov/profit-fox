using System.Text;
using System.Text.Json;

using ProfitFox.Engine.Strategy.Graph.Model;

namespace ProfitFox.Engine.Strategy.Graph.Reading;

/// <summary>
///     Reads and writes the JSON form of a graph (spec strategy-graph). Reading checks only the form: fields, their JSON
///     types and the version of the format. Unknown fields are errors, so a typo never changes a graph silently. Numbers go
///     from the JSON text straight to <see cref="decimal" />.
/// </summary>
internal static class GraphJson
{
    private static readonly string[] RootFields = ["formatVersion", "parameters", "outputs", "nodes", "wires"];
    private static readonly string[] ParameterFields = ["name", "type", "value", "minimum", "maximum"];
    private static readonly string[] OutputFields = ["name", "type"];
    private static readonly string[] NodeFields = ["id", "kind", "settings", "inputs"];
    private static readonly string[] WireFields = ["from", "to"];

    /// <summary>
    ///     The definition, or null with the errors added to <paramref name="errors" /> when the JSON has the wrong form.
    /// </summary>
    public static GraphDefinition? Read(JsonElement root, List<GraphError> errors)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new GraphError(GraphErrorCode.InvalidField, "The graph must be a JSON object."));

            return null;
        }

        int errorsBefore = errors.Count;
        Dictionary<string, JsonElement> fields = Fields(root, "", RootFields, errors);

        if (!fields.TryGetValue("formatVersion", out JsonElement version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out int number)
            || number != GraphDefinition.FormatVersion)
        {
            errors.Add(
                new GraphError(GraphErrorCode.UnsupportedFormatVersion, $"formatVersion must be {GraphDefinition.FormatVersion}.")
                {
                    Field = "formatVersion"
                });

            return null;
        }

        List<ParameterDefinition> parameters = Array(fields, "parameters", required: false, errors, ReadParameter);
        List<OutputDefinition> outputs = Array(fields, "outputs", required: false, errors, ReadOutput);
        List<NodeDefinition> nodes = Array(fields, "nodes", required: true, errors, ReadNode);
        List<WireDefinition> wires = Array(fields, "wires", required: false, errors, ReadWire);

        return errors.Count == errorsBefore ? new GraphDefinition(parameters, outputs, nodes, wires) : null;
    }

    public static string Write(GraphDefinition graph)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            Write(writer, graph);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Write(Utf8JsonWriter writer, GraphDefinition graph)
    {
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", GraphDefinition.FormatVersion);

        writer.WriteStartArray("parameters");

        foreach (ParameterDefinition parameter in graph.Parameters)
        {
            writer.WriteStartObject();
            writer.WriteString("name", parameter.Name);
            writer.WriteString("type", PinTypes.Name(parameter.Type));
            writer.WritePropertyName("value");
            WriteValue(writer, parameter.Value);

            if (parameter.Minimum is { } minimum)
            {
                writer.WriteNumber("minimum", minimum);
            }

            if (parameter.Maximum is { } maximum)
            {
                writer.WriteNumber("maximum", maximum);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("outputs");

        foreach (OutputDefinition output in graph.Outputs)
        {
            writer.WriteStartObject();
            writer.WriteString("name", output.Name);
            writer.WriteString("type", PinTypes.Name(output.Type));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("nodes");

        foreach (NodeDefinition node in graph.Nodes)
        {
            writer.WriteStartObject();
            writer.WriteString("id", node.Id);
            writer.WriteString("kind", node.Kind);

            if (node.Settings.Count > 0)
            {
                writer.WriteStartObject("settings");

                foreach ((string name, string value) in node.Settings)
                {
                    writer.WriteString(name, value);
                }

                writer.WriteEndObject();
            }

            if (node.Inputs.Count > 0)
            {
                writer.WriteStartObject("inputs");

                foreach ((string name, GraphValue value) in node.Inputs)
                {
                    writer.WritePropertyName(name);
                    WriteValue(writer, value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("wires");

        foreach (WireDefinition wire in graph.Wires)
        {
            writer.WriteStartObject();
            writer.WriteString("from", wire.From.ToString());
            writer.WriteString("to", wire.To.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, GraphValue value)
    {
        switch (value.Type)
        {
            case PinType.Number:
                writer.WriteNumberValue(value.Number);

                break;
            case PinType.Boolean:
                writer.WriteBooleanValue(value.Boolean);

                break;
            case PinType.Duration:
                writer.WriteStringValue(Durations.Format(value.Duration));

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value.Type, "Only numbers, booleans and durations are written.");
        }
    }

    private static ParameterDefinition? ReadParameter(JsonElement element, string path, List<GraphError> errors)
    {
        int errorsBefore = errors.Count;
        Dictionary<string, JsonElement> fields = Fields(element, path, ParameterFields, errors);
        string? name = RequiredString(fields, path, "name", errors);
        PinType? type = RequiredType(fields, path, "type", errors);
        GraphValue? value = null;

        if (fields.TryGetValue("value", out JsonElement valueElement))
        {
            value = ReadValue(valueElement, Join(path, "value"), errors);
        }
        else
        {
            Missing(path, "value", errors);
        }

        decimal? minimum = OptionalNumber(fields, path, "minimum", errors);
        decimal? maximum = OptionalNumber(fields, path, "maximum", errors);

        return errors.Count == errorsBefore
            ? new ParameterDefinition(name!, type!.Value, value!.Value, minimum, maximum)
            : null;
    }

    private static OutputDefinition? ReadOutput(JsonElement element, string path, List<GraphError> errors)
    {
        int errorsBefore = errors.Count;
        Dictionary<string, JsonElement> fields = Fields(element, path, OutputFields, errors);
        string? name = RequiredString(fields, path, "name", errors);
        PinType? type = RequiredType(fields, path, "type", errors);

        return errors.Count == errorsBefore ? new OutputDefinition(name!, type!.Value) : null;
    }

    private static NodeDefinition? ReadNode(JsonElement element, string path, List<GraphError> errors)
    {
        int errorsBefore = errors.Count;
        Dictionary<string, JsonElement> fields = Fields(element, path, NodeFields, errors);
        string? id = RequiredString(fields, path, "id", errors);
        string? kind = RequiredString(fields, path, "kind", errors);

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);

        if (fields.TryGetValue("settings", out JsonElement settingsElement))
        {
            string settingsPath = Join(path, "settings");

            if (settingsElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add(Invalid(settingsPath, "settings must be an object."));
            }
            else
            {
                foreach (JsonProperty setting in settingsElement.EnumerateObject())
                {
                    string settingPath = Join(settingsPath, setting.Name);

                    if (setting.Value.ValueKind != JsonValueKind.String)
                    {
                        errors.Add(Invalid(settingPath, "A setting is a string."));
                    }
                    else if (!settings.TryAdd(setting.Name, setting.Value.GetString()!))
                    {
                        errors.Add(Duplicate(settingPath, setting.Name));
                    }
                }
            }
        }

        var inputs = new Dictionary<string, GraphValue>(StringComparer.Ordinal);

        if (fields.TryGetValue("inputs", out JsonElement inputsElement))
        {
            string inputsPath = Join(path, "inputs");

            if (inputsElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add(Invalid(inputsPath, "inputs must be an object."));
            }
            else
            {
                foreach (JsonProperty input in inputsElement.EnumerateObject())
                {
                    string inputPath = Join(inputsPath, input.Name);
                    GraphValue? value = ReadValue(input.Value, inputPath, errors);

                    if (value is not null && !inputs.TryAdd(input.Name, value.Value))
                    {
                        errors.Add(Duplicate(inputPath, input.Name));
                    }
                }
            }
        }

        return errors.Count == errorsBefore ? new NodeDefinition(id!, kind!, settings, inputs) : null;
    }

    private static WireDefinition? ReadWire(JsonElement element, string path, List<GraphError> errors)
    {
        int errorsBefore = errors.Count;
        Dictionary<string, JsonElement> fields = Fields(element, path, WireFields, errors);
        PinReference? from = RequiredPin(fields, path, "from", errors);
        PinReference? to = RequiredPin(fields, path, "to", errors);

        return errors.Count == errorsBefore ? new WireDefinition(from!, to!) : null;
    }

    // A number, a boolean or a duration; the type it must have is checked against the pin or the parameter later.
    private static GraphValue? ReadValue(JsonElement element, string path, List<GraphError> errors)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetDecimal(out decimal number):
                return GraphValue.FromNumber(number);
            case JsonValueKind.True:
                return GraphValue.FromBoolean(true);
            case JsonValueKind.False:
                return GraphValue.FromBoolean(false);
            case JsonValueKind.String when Durations.TryParse(element.GetString()!, out TimeSpan duration):
                return GraphValue.FromDuration(duration);
            default:
                errors.Add(Invalid(path, "A value is a number, true, false or a duration such as 4h."));

                return null;
        }
    }

    private static Dictionary<string, JsonElement> Fields(JsonElement element, string path, string[] known, List<GraphError> errors)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (JsonProperty property in element.EnumerateObject())
        {
            string field = Join(path, property.Name);

            if (!known.Contains(property.Name, StringComparer.Ordinal))
            {
                errors.Add(new GraphError(GraphErrorCode.UnknownField, $"Unknown field {property.Name}.") { Field = field });
            }
            else if (!fields.TryAdd(property.Name, property.Value))
            {
                errors.Add(Duplicate(field, property.Name));
            }
        }

        return fields;
    }

    private static List<T> Array<T>(
        Dictionary<string, JsonElement> fields,
        string name,
        bool required,
        List<GraphError> errors,
        Func<JsonElement, string, List<GraphError>, T?> read)
        where T : class
    {
        List<T> items = [];

        if (!fields.TryGetValue(name, out JsonElement array))
        {
            if (required)
            {
                Missing("", name, errors);
            }

            return items;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Invalid(name, $"{name} must be an array."));

            return items;
        }

        var index = 0;

        foreach (JsonElement element in array.EnumerateArray())
        {
            var path = $"{name}[{index}]";
            index++;

            if (element.ValueKind != JsonValueKind.Object)
            {
                errors.Add(Invalid(path, "Each item must be an object."));

                continue;
            }

            T? item = read(element, path, errors);

            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static string? RequiredString(Dictionary<string, JsonElement> fields, string path, string name, List<GraphError> errors)
    {
        if (!fields.TryGetValue(name, out JsonElement element))
        {
            Missing(path, name, errors);

            return null;
        }

        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { Length: > 0 } text)
        {
            errors.Add(Invalid(Join(path, name), $"{name} must be a non-empty string."));

            return null;
        }

        return text;
    }

    private static PinType? RequiredType(Dictionary<string, JsonElement> fields, string path, string name, List<GraphError> errors)
    {
        string? text = RequiredString(fields, path, name, errors);

        if (text is null)
        {
            return null;
        }

        if (!PinTypes.TryParse(text, out PinType type))
        {
            errors.Add(Invalid(Join(path, name), "The type is number, boolean, duration or event."));

            return null;
        }

        return type;
    }

    private static PinReference? RequiredPin(Dictionary<string, JsonElement> fields, string path, string name, List<GraphError> errors)
    {
        string? text = RequiredString(fields, path, name, errors);

        if (text is null)
        {
            return null;
        }

        if (!PinReference.TryParse(text, out PinReference? reference))
        {
            errors.Add(Invalid(Join(path, name), "A pin is written node.pin."));

            return null;
        }

        return reference;
    }

    private static decimal? OptionalNumber(Dictionary<string, JsonElement> fields, string path, string name, List<GraphError> errors)
    {
        if (!fields.TryGetValue(name, out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out decimal number))
        {
            return number;
        }

        errors.Add(Invalid(Join(path, name), $"{name} must be a number."));

        return null;
    }

    private static void Missing(string path, string name, List<GraphError> errors)
    {
        errors.Add(new GraphError(GraphErrorCode.MissingField, $"The field {name} is missing.") { Field = Join(path, name) });
    }

    private static GraphError Invalid(string field, string reason)
    {
        return new GraphError(GraphErrorCode.InvalidField, reason) { Field = field };
    }

    private static GraphError Duplicate(string field, string name)
    {
        return new GraphError(GraphErrorCode.DuplicateField, $"{name} appears twice.") { Field = field };
    }

    private static string Join(string path, string name)
    {
        return path.Length == 0 ? name : $"{path}.{name}";
    }
}