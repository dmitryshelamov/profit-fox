using System.Text.Json;

using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Presentation.Contracts.Strategies.Converters;

/// <summary>
///     A saved strategy and what its signal nodes point at, as the API sends them.
/// </summary>
public static class StrategyResponses
{
    public static StrategyResponse Of(StrategyInternal strategy)
    {
        using var graph = JsonDocument.Parse(StrategyGraph.Write(strategy.Graph));
        return new StrategyResponse(
            strategy.Name,
            strategy.Description,
            strategy.Version,
            graph.RootElement.Clone(),
            strategy.Layout,
            [.. strategy.Signals.Select(Of)],
            strategy.Raised,
            StrategyKinds.Name(strategy.Kind));
    }

    public static SignalUseResponse Of(SignalUseInternal use)
    {
        return new SignalUseResponse(use.Node, use.Signal, use.Name, use.Version, use.LatestVersion, [.. use.Parameters.Select(Of)]);
    }

    private static SignalParameterResponse Of(ParameterDefinition parameter)
    {
        return new SignalParameterResponse(parameter.Name, PinTypes.Name(parameter.Type), Value(parameter.Value), parameter.Minimum, parameter.Maximum);
    }

    // The value as the JSON of a graph writes it: a number, true or false, or a duration such as «4h».
    private static JsonElement Value(GraphValue value)
    {
        return value.Type switch
        {
            PinType.Number => JsonSerializer.SerializeToElement(value.Number),
            PinType.Boolean => JsonSerializer.SerializeToElement(value.Boolean),
            _ => JsonSerializer.SerializeToElement(Durations.Format(value.Duration))
        };
    }
}
