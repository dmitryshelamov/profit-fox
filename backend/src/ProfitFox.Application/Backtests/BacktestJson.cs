using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Backtests;

/// <summary>
///     The JSON form of the parameters of a backtest, the API body and the stored value alike: a value as it is, a range as
///     <c>{ "min": …, "max": …, "step": … }</c>, such as <c>{ "fast": { "min": 5, "max": 15, "step": 5 }, "slow": 26 }</c>.
/// </summary>
public static class BacktestJson
{
    public static JsonObject WriteParameters(IReadOnlyDictionary<string, ParameterSpec> parameters)
    {
        return new JsonObject(parameters.Select(parameter => KeyValuePair.Create(parameter.Key, Write(parameter.Value))));
    }

    /// <summary>Reads stored parameters; they were valid when written.</summary>
    public static IReadOnlyDictionary<string, ParameterSpec> ReadParameters(JsonElement json)
    {
        return json.EnumerateObject().ToDictionary(parameter => parameter.Name, parameter => Read(parameter.Value)!);
    }

    /// <summary>A value or a range; <c>null</c> when the JSON is neither.</summary>
    public static ParameterSpec? Read(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return StrategyJson.ParseValue(json) is { } value ? ParameterSpec.Of(value) : null;
        }

        NodeValue? min = Property(json, "min");
        NodeValue? max = Property(json, "max");
        NodeValue? step = Property(json, "step");

        return min is null || max is null || step is null ? null : ParameterSpec.Between(min, max, step);
    }

    private static NodeValue? Property(JsonElement json, string name)
    {
        return json.TryGetProperty(name, out JsonElement value) ? StrategyJson.ParseValue(value) : null;
    }

    private static JsonNode? Write(ParameterSpec spec)
    {
        return spec.Range is { } range
            ? new JsonObject { ["min"] = StrategyJson.WriteValue(range.Min), ["max"] = StrategyJson.WriteValue(range.Max), ["step"] = StrategyJson.WriteValue(range.Step) }
            : StrategyJson.WriteValue(spec.Value!);
    }
}