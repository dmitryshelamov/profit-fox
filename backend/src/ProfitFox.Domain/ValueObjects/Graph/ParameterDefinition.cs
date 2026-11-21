using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.ValueObjects.Graph;

/// <summary>
///     A parameter of the graph: a named value the <c>parameters</c> node gives as an output, with optional bounds for a
///     number.
/// </summary>
public sealed record ParameterDefinition(string Name, PinType Type, GraphValue Value, decimal? Minimum, decimal? Maximum);