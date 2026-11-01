using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies;

/// <summary>
///     The JSON form of strategy and composite node definitions, used both as the API body and as the stored value.
///     Reading reports every problem under its path, such as <c>definition.nodes[2].kind</c>.
/// </summary>
public static partial class StrategyJson
{
    private const string Root = "definition";
    private const int FormatVersion = 1;
    private const string DurationFormat = "c";

    /// <exception cref="ValidationFailedException">The document is not a strategy definition.</exception>
    public static StrategyDefinition ParseStrategy(JsonElement json)
    {
        return Read(json, (reader, root) =>
        {
            StrategyDirection direction = reader.Code<StrategyDirection>(root, "direction", $"{Root}.direction", required: true);
            IReadOnlyList<StrategyParameter> parameters = reader.Parameters(root);

            return new StrategyDefinition(direction, parameters, reader.Graph(root));
        });
    }

    /// <exception cref="ValidationFailedException">The document is not a composite node definition.</exception>
    public static CompositeDefinition ParseComposite(JsonElement json)
    {
        return Read(json, (reader, root) =>
        {
            IReadOnlyList<PinDeclaration> inputs = reader.Pins(root, "inputs");
            IReadOnlyList<PinDeclaration> outputs = reader.Pins(root, "outputs");

            return new CompositeDefinition(inputs, outputs, reader.Graph(root));
        });
    }

    /// <summary>A value of a wrong JSON kind, or a number outside <see cref="decimal" />, stops reading with one error.</summary>
    private static T Read<T>(JsonElement json, Func<Reader, JsonElement, T> read)
    {
        var reader = new Reader();
        T? definition = default;

        try
        {
            if (reader.Header(json))
            {
                definition = read(reader, json);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            throw new ValidationFailedException(new Dictionary<string, string> { [Root] = "Definition has a value of a wrong JSON type." });
        }

        reader.ThrowIfFailed();

        return definition!;
    }

    public static JsonObject WriteStrategy(StrategyDefinition definition)
    {
        var json = new JsonObject
        {
            ["formatVersion"] = FormatVersion,
            ["direction"] = StrategyCodes.ToCode(definition.Direction),
            ["parameters"] = new JsonArray([.. definition.Parameters.Select(WriteParameter)])
        };
        WriteGraph(definition.Graph, json);

        return json;
    }

    public static JsonObject WriteComposite(CompositeDefinition definition)
    {
        var json = new JsonObject
        {
            ["formatVersion"] = FormatVersion,
            ["inputs"] = new JsonArray([.. definition.Inputs.Select(WritePin)]),
            ["outputs"] = new JsonArray([.. definition.Outputs.Select(WritePin)])
        };
        WriteGraph(definition.Graph, json);

        return json;
    }

    /// <summary>A strategy version: <c>{ "strategy": …, "composites": [ { "ref": …, "definition": … } ] }</c>.</summary>
    public static JsonObject WriteSnapshot(StrategySnapshot snapshot)
    {
        return new JsonObject
        {
            ["strategy"] = WriteStrategy(snapshot.Definition),
            ["composites"] = new JsonArray(
            [
                .. snapshot.Composites.Select(composite => new JsonObject { ["ref"] = ToCode(composite.Id), ["definition"] = WriteComposite(composite.Definition) })
            ])
        };
    }

    /// <summary>Reads a stored version; it was valid when written, so it is not checked again.</summary>
    public static StrategySnapshot ParseSnapshot(JsonElement json)
    {
        IReadOnlyList<SnapshotComposite> composites =
        [
            .. json.GetProperty("composites").EnumerateArray().Select(composite =>
            {
                TryParseNodeId(composite.GetProperty("ref").GetString()!, out CompositeNodeId? id);

                return new SnapshotComposite(id!, ParseComposite(composite.GetProperty("definition")));
            })
        ];

        return StrategySnapshot.Restore(ParseStrategy(json.GetProperty("strategy")), composites);
    }

    /// <summary>Parameter values of a bot or a backtest: <c>{ "fast": 10, "every": "01:00:00" }</c>.</summary>
    public static JsonObject WriteValues(IReadOnlyDictionary<string, NodeValue> values)
    {
        return new JsonObject(values.Select(value => KeyValuePair.Create(value.Key, WriteValue(value.Value))));
    }

    /// <summary>A number, <c>true</c>/<c>false</c> or a duration such as <c>01:00:00</c>; <c>null</c> for anything else.</summary>
    public static NodeValue? ParseValue(JsonElement json)
    {
        return json.ValueKind switch
        {
            JsonValueKind.Number => new NumberValue(json.GetDecimal()),
            JsonValueKind.True or JsonValueKind.False => new BooleanValue(json.GetBoolean()),
            JsonValueKind.String when TimeSpan.TryParseExact(json.GetString(), DurationFormat, CultureInfo.InvariantCulture, out TimeSpan duration) =>
                new DurationValue(duration),
            _ => null
        };
    }

    /// <summary>Reads stored values; they were valid when written.</summary>
    public static IReadOnlyDictionary<string, NodeValue> ParseValues(JsonElement json)
    {
        return json.EnumerateObject().ToDictionary(value => value.Name, value => ParseValue(value.Value)!);
    }

    /// <summary>Code of a composite node id in a graph reference: the built-in code or the custom <see cref="Guid" />.</summary>
    public static string ToCode(CompositeNodeId id)
    {
        return id is BuiltInNodeId builtIn ? StrategyCodes.ToCode(builtIn.Node) : ((CustomNodeId)id).Value.ToString();
    }

    public static bool TryParseNodeId(string code, out CompositeNodeId? id)
    {
        if (StrategyCodes.TryParse(code, out BuiltInNode node))
        {
            id = new BuiltInNodeId(node);

            return true;
        }

        id = Guid.TryParse(code, out Guid custom) ? new CustomNodeId(custom) : null;

        return id is not null;
    }

    private static JsonObject WriteParameter(StrategyParameter parameter)
    {
        var json = new JsonObject
        {
            ["name"] = parameter.Name,
            ["type"] = StrategyCodes.ToCode(parameter.Type),
            ["default"] = WriteValue(parameter.Default)
        };

        if (parameter.Min is { } min)
        {
            json["min"] = min;
        }

        if (parameter.Max is { } max)
        {
            json["max"] = max;
        }

        if (parameter.Integer)
        {
            json["integer"] = true;
        }

        return json;
    }

    private static JsonObject WritePin(PinDeclaration pin)
    {
        return new JsonObject { ["name"] = pin.Name, ["type"] = StrategyCodes.ToCode(pin.Type) };
    }

    private static void WriteGraph(Graph graph, JsonObject json)
    {
        json["nodes"] = new JsonArray([.. graph.Nodes.Select(WriteNode)]);
        json["wires"] = new JsonArray(
        [
            .. graph.Wires.Select(wire => new JsonObject { ["from"] = $"{wire.From.Node}.{wire.From.Pin}", ["to"] = $"{wire.To.Node}.{wire.To.Pin}" })
        ]);
        json["layout"] = new JsonObject(graph.Layout.Select(position => KeyValuePair.Create<string, JsonNode?>(
            position.Key,
            new JsonObject { ["x"] = position.Value.X, ["y"] = position.Value.Y })));
    }

    private static JsonObject WriteNode(GraphNode node)
    {
        var json = new JsonObject { ["id"] = node.Id };

        if (node.Type is SimpleNodeType simple)
        {
            json["kind"] = StrategyCodes.ToCode(simple.Kind);
        }
        else
        {
            json["kind"] = "composite";
            json["ref"] = ToCode(((CompositeNodeType)node.Type).Id);
        }

        var settings = new JsonObject();
        AddCode(settings, "operation", node.Settings.Operation);
        AddCode(settings, "type", node.Settings.ValueType);
        AddCode(settings, "side", node.Settings.Side);
        AddCode(settings, "size", node.Settings.SizeMode);

        if (settings.Count > 0)
        {
            json["settings"] = settings;
        }

        if (node.Inputs.Count > 0)
        {
            json["inputs"] = new JsonObject(node.Inputs.Select(input => KeyValuePair.Create(input.Key, WriteValue(input.Value))));
        }

        return json;
    }

    private static void AddCode(JsonObject json, string name, Enum? value)
    {
        if (value is not null)
        {
            json[name] = StrategyCodes.ToCode(value);
        }
    }

    public static JsonNode? WriteValue(NodeValue value)
    {
        return value switch
        {
            NumberValue number => JsonValue.Create(number.Value),
            BooleanValue boolean => JsonValue.Create(boolean.Value),
            _ => JsonValue.Create(((DurationValue)value).Value.ToString(DurationFormat, CultureInfo.InvariantCulture))
        };
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex IdPattern();

    /// <summary>Reads a definition and collects every problem instead of stopping at the first one.</summary>
    private sealed class Reader
    {
        private readonly Dictionary<string, string> _errors = [];

        public bool Header(JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Object)
            {
                Fail(Root, "Definition must be a JSON object.");

                return false;
            }

            if (!json.TryGetProperty("formatVersion", out JsonElement version) || !version.TryGetDecimal(out decimal number) || number != FormatVersion)
            {
                Fail($"{Root}.formatVersion", $"Format version must be {FormatVersion}.");
            }

            return true;
        }

        public void ThrowIfFailed()
        {
            if (_errors.Count > 0)
            {
                throw new ValidationFailedException(_errors);
            }
        }

        public T Code<T>(JsonElement json, string property, string path, bool required)
            where T : struct, Enum
        {
            if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(property, out JsonElement value))
            {
                if (required)
                {
                    Fail(path, "Value is required.");
                }

                return default;
            }

            if (value.ValueKind != JsonValueKind.String || !StrategyCodes.TryParse(value.GetString()!, out T parsed))
            {
                Fail(path, $"Unknown code {value}.");

                return default;
            }

            return parsed;
        }

        public IReadOnlyList<StrategyParameter> Parameters(JsonElement json)
        {
            var parameters = new List<StrategyParameter>();
            int index = 0;

            foreach (JsonElement parameter in Array(json, "parameters", required: false))
            {
                string path = $"{Root}.parameters[{index++}]";
                string name = Name(parameter, $"{path}.name");
                PinType type = Code<PinType>(parameter, "type", $"{path}.type", required: true);

                if (type == PinType.Boolean)
                {
                    Fail($"{path}.type", "Parameter type must be number or duration.");
                }

                NodeValue value = parameter.TryGetProperty("default", out JsonElement raw) ? Value(raw, $"{path}.default", type) : Missing($"{path}.default");
                decimal? min = parameter.TryGetProperty("min", out JsonElement low) ? low.GetDecimal() : null;
                decimal? max = parameter.TryGetProperty("max", out JsonElement high) ? high.GetDecimal() : null;
                bool integer = parameter.TryGetProperty("integer", out JsonElement whole) && whole.GetBoolean();
                parameters.Add(new StrategyParameter(name, type, value, min, max, integer));
            }

            return parameters;
        }

        public IReadOnlyList<PinDeclaration> Pins(JsonElement json, string property)
        {
            var pins = new List<PinDeclaration>();
            int index = 0;

            foreach (JsonElement pin in Array(json, property, required: true))
            {
                string path = $"{Root}.{property}[{index++}]";
                pins.Add(new PinDeclaration(Name(pin, $"{path}.name"), Code<PinType>(pin, "type", $"{path}.type", required: true)));
            }

            return pins;
        }

        public Graph Graph(JsonElement json)
        {
            var nodes = new List<GraphNode>();
            int index = 0;

            foreach (JsonElement node in Array(json, "nodes", required: true))
            {
                nodes.Add(Node(node, $"{Root}.nodes[{index++}]"));
            }

            var wires = new List<Wire>();
            index = 0;

            foreach (JsonElement wire in Array(json, "wires", required: false))
            {
                string path = $"{Root}.wires[{index++}]";
                wires.Add(new Wire(Pin(wire, "from", $"{path}.from"), Pin(wire, "to", $"{path}.to")));
            }

            var layout = new Dictionary<string, NodePosition>();

            if (json.TryGetProperty("layout", out JsonElement positions))
            {
                foreach (JsonProperty position in positions.EnumerateObject())
                {
                    if (position.Value.TryGetProperty("x", out JsonElement x) && position.Value.TryGetProperty("y", out JsonElement y))
                    {
                        layout[position.Name] = new NodePosition(x.GetDecimal(), y.GetDecimal());
                    }
                    else
                    {
                        Fail($"{Root}.layout.{position.Name}", "Position needs x and y.");
                    }
                }
            }

            return new Graph(nodes, wires, layout);
        }

        private GraphNode Node(JsonElement json, string path)
        {
            string id = json.TryGetProperty("id", out JsonElement raw) && raw.ValueKind == JsonValueKind.String ? raw.GetString()! : "";

            if (!IdPattern().IsMatch(id))
            {
                Fail($"{path}.id", "Node id must consist of letters, digits, _ and -.");
            }

            NodeType type = Type(json, path);
            NodeSettings settings = Settings(json, type, $"{path}.settings");
            var inputs = new Dictionary<string, NodeValue>();

            if (json.TryGetProperty("inputs", out JsonElement values))
            {
                foreach (JsonProperty input in values.EnumerateObject())
                {
                    inputs[input.Name] = Value(input.Value, $"{path}.inputs.{input.Name}", null);
                }
            }

            return new GraphNode(id, type, inputs, settings);
        }

        private NodeType Type(JsonElement json, string path)
        {
            string kind = json.TryGetProperty("kind", out JsonElement raw) && raw.ValueKind == JsonValueKind.String ? raw.GetString()! : "";

            if (kind == "composite")
            {
                string reference = json.TryGetProperty("ref", out JsonElement code) && code.ValueKind == JsonValueKind.String ? code.GetString()! : "";

                if (TryParseNodeId(reference, out CompositeNodeId? id))
                {
                    return new CompositeNodeType(id!);
                }

                Fail($"{path}.ref", "Reference must be a built-in node code or a custom node id.");

                return new SimpleNodeType(SimpleNodeKind.Candle);
            }

            if (StrategyCodes.TryParse(kind, out SimpleNodeKind simple))
            {
                return new SimpleNodeType(simple);
            }

            Fail($"{path}.kind", $"Unknown node kind {kind}.");

            return new SimpleNodeType(SimpleNodeKind.Candle);
        }

        private NodeSettings Settings(JsonElement json, NodeType type, string path)
        {
            JsonElement settings = json.TryGetProperty("settings", out JsonElement raw) ? raw : default;
            var result = new NodeSettings();

            if (type is not SimpleNodeType simple)
            {
                return result;
            }

            foreach (NodeSettingSpec spec in NodeSettingSpecs.Of(simple.Kind))
            {
                if (Setting(settings, spec, $"{path}.{spec.Name}") is { } value)
                {
                    result = spec.Apply(result, value);
                }
            }

            return result;
        }

        private Enum? Setting(JsonElement settings, NodeSettingSpec spec, string path)
        {
            if (settings.ValueKind != JsonValueKind.Object || !settings.TryGetProperty(spec.Name, out JsonElement value))
            {
                if (spec.Required)
                {
                    Fail(path, "Value is required.");
                }

                return null;
            }

            if (value.ValueKind != JsonValueKind.String || !StrategyCodes.TryParse(spec.Default.GetType(), value.GetString()!, out Enum? parsed))
            {
                Fail(path, $"Unknown code {value}.");

                return null;
            }

            if (!spec.Values.Contains(parsed!))
            {
                Fail(path, $"Value must be one of {string.Join(", ", spec.Values.Select(StrategyCodes.ToCode))}.");

                return null;
            }

            return parsed;
        }

        private PinRef Pin(JsonElement json, string property, string path)
        {
            string[] parts = json.TryGetProperty(property, out JsonElement raw) && raw.ValueKind == JsonValueKind.String ? raw.GetString()!.Split('.') : [];

            if (parts.Length != 2)
            {
                Fail(path, "Pin must be written as node.pin.");

                return new PinRef("", "");
            }

            return new PinRef(parts[0], parts[1]);
        }

        /// <summary>A number, <c>true</c>/<c>false</c> or a duration such as <c>01:00:00</c>; <paramref name="expected" /> narrows it to one type.</summary>
        private NodeValue Value(JsonElement json, string path, PinType? expected)
        {
            NodeValue? value = ParseValue(json);

            if (value is null || (expected is { } type && value.Type != type))
            {
                Fail(path, "Value must be a number, true, false or a duration such as 01:00:00 of the declared type.");

                return new NumberValue(0m);
            }

            return value;
        }

        private string Name(JsonElement json, string path)
        {
            string name = json.TryGetProperty("name", out JsonElement raw) && raw.ValueKind == JsonValueKind.String ? raw.GetString()! : "";

            if (!IdPattern().IsMatch(name))
            {
                Fail(path, "Name must consist of letters, digits, _ and -.");
            }

            return name;
        }

        private IEnumerable<JsonElement> Array(JsonElement json, string property, bool required)
        {
            if (json.TryGetProperty(property, out JsonElement array) && array.ValueKind == JsonValueKind.Array)
            {
                return array.EnumerateArray();
            }

            if (required)
            {
                Fail($"{Root}.{property}", "Array is required.");
            }

            return [];
        }

        private NodeValue Missing(string path)
        {
            Fail(path, "Value is required.");

            return new NumberValue(0m);
        }

        private void Fail(string path, string message)
        {
            _errors.TryAdd(path, message);
        }
    }
}