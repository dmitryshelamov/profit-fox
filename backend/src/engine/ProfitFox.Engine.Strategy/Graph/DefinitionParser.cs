using System.Text.Json;

namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     Turns the JSON text of a composite node into its definition, checking only the form of the JSON: the fields, their JSON types and
///     the version of the format. What the nodes and wires mean is checked by <see cref="GraphValidator" />.
/// </summary>
internal static class DefinitionParser
{
    private const int SupportedFormatVersion = 1;

    private static readonly string[] RootFields = ["formatVersion", "outputs", "nodes", "wires"];
    private static readonly string[] OutputFields = ["name", "type"];
    private static readonly string[] NodeFields = ["id", "kind", "settings", "inputs"];
    private static readonly string[] WireFields = ["from", "to"];

    /// <summary>
    ///     The definition, or null with the errors added to <paramref name="errors" /> when the JSON has the wrong form.
    /// </summary>
    public static CompositeNodeDefinition? Parse(string json, List<GraphError> errors)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            errors.Add(new GraphError(GraphErrorCode.NotJson, $"The text is not JSON: {exception.Message}"));
            return null;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new GraphError(GraphErrorCode.InvalidField, "The definition must be a JSON object."));
                return null;
            }

            int errorsBefore = errors.Count;
            Dictionary<string, JsonElement> fields = Fields(root, path: "", RootFields, errors);
            if (!IsSupportedVersion(fields))
            {
                errors.Add(new GraphError(GraphErrorCode.UnsupportedFormatVersion, $"formatVersion must be {SupportedFormatVersion}.")
                {
                    Field = "formatVersion"
                });
                return null;
            }

            List<PinDeclaration> outputs = Array(fields, "outputs", errors, ParseOutput);
            List<NodeDefinition> nodes = Array(fields, "nodes", errors, ParseNode);
            List<WireDefinition> wires = Array(fields, "wires", errors, ParseWire);

            return errors.Count == errorsBefore ? new CompositeNodeDefinition(outputs, nodes, wires) : null;
        }
    }

    private static bool IsSupportedVersion(Dictionary<string, JsonElement> fields)
    {
        return fields.TryGetValue("formatVersion", out JsonElement version)
               && version.ValueKind == JsonValueKind.Number
               && version.TryGetInt32(out int number)
               && number == SupportedFormatVersion;
    }

    // The fields of an object by name; unknown and repeated fields are errors.
    private static Dictionary<string, JsonElement> Fields(
        JsonElement element,
        string path,
        string[] known,
        List<GraphError> errors,
        string? node = null)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            string field = Join(path, property.Name);
            if (!known.Contains(property.Name, StringComparer.Ordinal))
            {
                errors.Add(new GraphError(GraphErrorCode.UnknownField, $"Unknown field {property.Name}.") { Field = field, Node = node });
            }
            else if (!fields.TryAdd(property.Name, property.Value))
            {
                errors.Add(new GraphError(GraphErrorCode.DuplicateField, $"The field {property.Name} appears twice.") { Field = field, Node = node });
            }
        }

        return fields;
    }

    private static List<T> Array<T>(
        Dictionary<string, JsonElement> fields,
        string name,
        List<GraphError> errors,
        Func<JsonElement, string, List<GraphError>, T?> parse)
        where T : class
    {
        List<T> items = [];
        if (!fields.TryGetValue(name, out JsonElement array))
        {
            errors.Add(new GraphError(GraphErrorCode.MissingField, $"The field {name} is missing.") { Field = name });
            return items;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new GraphError(GraphErrorCode.InvalidField, $"The field {name} must be an array.") { Field = name });
            return items;
        }

        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            string path = $"{name}[{index}]";
            index++;
            if (element.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new GraphError(GraphErrorCode.InvalidField, $"{path} must be an object.") { Field = path });
                continue;
            }

            T? item = parse(element, path, errors);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static PinDeclaration? ParseOutput(JsonElement element, string path, List<GraphError> errors)
    {
        Dictionary<string, JsonElement> fields = Fields(element, path, OutputFields, errors);
        string? name = RequiredString(fields, path, "name", errors);
        string? typeName = RequiredString(fields, path, "type", errors);
        if (name is null || typeName is null)
        {
            return null;
        }

        PinType? type = ParsePinType(typeName);
        if (type is null)
        {
            errors.Add(new GraphError(GraphErrorCode.InvalidField, $"Unknown type {typeName}; the types are number and boolean.")
            {
                Field = Join(path, "type"),
                Pin = name
            });
            return null;
        }

        return new PinDeclaration(name, type.Value);
    }

    private static NodeDefinition? ParseNode(JsonElement element, string path, List<GraphError> errors)
    {
        int errorsBefore = errors.Count;
        string? named = element.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()
            : null;
        Dictionary<string, JsonElement> fields = Fields(element, path, NodeFields, errors, named);
        string? id = RequiredString(fields, path, "id", errors);
        string? kind = RequiredString(fields, path, "kind", errors);

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (fields.TryGetValue("settings", out JsonElement settingsElement))
        {
            string settingsPath = Join(path, "settings");
            foreach ((string name, JsonElement value) in ObjectEntries(settingsElement, settingsPath, errors))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    settings[name] = value.GetString()!;
                }
                else
                {
                    errors.Add(new GraphError(GraphErrorCode.InvalidField, $"The setting {name} must be a string.")
                    {
                        Field = Join(settingsPath, name),
                        Node = id,
                        Setting = name
                    });
                }
            }
        }

        var inputs = new Dictionary<string, PinValue>(StringComparer.Ordinal);
        if (fields.TryGetValue("inputs", out JsonElement inputsElement))
        {
            string inputsPath = Join(path, "inputs");
            foreach ((string name, JsonElement value) in ObjectEntries(inputsElement, inputsPath, errors))
            {
                PinValue? pinValue = ParseValue(value);
                if (pinValue is null)
                {
                    errors.Add(new GraphError(GraphErrorCode.InvalidField, $"The value of the input {name} must be a decimal number, true or false.")
                    {
                        Field = Join(inputsPath, name),
                        Node = id,
                        Pin = name
                    });
                }
                else
                {
                    inputs[name] = pinValue.Value;
                }
            }
        }

        return errors.Count == errorsBefore ? new NodeDefinition(id!, kind!, settings, inputs) : null;
    }

    private static WireDefinition? ParseWire(JsonElement element, string path, List<GraphError> errors)
    {
        Dictionary<string, JsonElement> fields = Fields(element, path, WireFields, errors);
        PinReference? from = RequiredPin(fields, path, "from", errors);
        PinReference? to = RequiredPin(fields, path, "to", errors);
        return from is null || to is null ? null : new WireDefinition(from, to);
    }

    private static PinReference? RequiredPin(Dictionary<string, JsonElement> fields, string path, string name, List<GraphError> errors)
    {
        string? text = RequiredString(fields, path, name, errors);
        if (text is null)
        {
            return null;
        }

        string[] parts = text.Split('.');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            errors.Add(new GraphError(GraphErrorCode.InvalidField, $"{text} is not of the form node.pin.") { Field = Join(path, name) });
            return null;
        }

        return new PinReference(parts[0], parts[1]);
    }

    private static string? RequiredString(Dictionary<string, JsonElement> fields, string path, string name, List<GraphError> errors)
    {
        string field = Join(path, name);
        if (!fields.TryGetValue(name, out JsonElement value))
        {
            errors.Add(new GraphError(GraphErrorCode.MissingField, $"The field {name} is missing.") { Field = field });
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add(new GraphError(GraphErrorCode.InvalidField, $"The field {name} must be a string.") { Field = field });
            return null;
        }

        return value.GetString();
    }

    // The entries of a JSON object of settings or input values; a repeated name is an error.
    private static List<(string Name, JsonElement Value)> ObjectEntries(JsonElement element, string path, List<GraphError> errors)
    {
        List<(string Name, JsonElement Value)> entries = [];
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new GraphError(GraphErrorCode.InvalidField, $"{path} must be an object.") { Field = path });
            return entries;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (seen.Add(property.Name))
            {
                entries.Add((property.Name, property.Value));
            }
            else
            {
                errors.Add(new GraphError(GraphErrorCode.DuplicateField, $"The field {property.Name} appears twice.")
                {
                    Field = Join(path, property.Name)
                });
            }
        }

        return entries;
    }

    // A number is read straight into decimal, never through double, so 0.0005 stays exactly 0.0005.
    private static PinValue? ParseValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out decimal number) => PinValue.FromNumber(number),
            JsonValueKind.True => PinValue.FromBoolean(true),
            JsonValueKind.False => PinValue.FromBoolean(false),
            _ => null
        };
    }

    private static PinType? ParsePinType(string name)
    {
        return name switch
        {
            "number" => PinType.Number,
            "boolean" => PinType.Boolean,
            _ => null
        };
    }

    private static string Join(string path, string name)
    {
        return path.Length == 0 ? name : $"{path}.{name}";
    }
}