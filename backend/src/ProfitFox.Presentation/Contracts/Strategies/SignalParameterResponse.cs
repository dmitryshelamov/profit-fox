using System.Text.Json;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>
///     A parameter of the version of a signal a node points at: its name, type, value as the graph writes it, and the bounds
///     of a number.
/// </summary>
public sealed record SignalParameterResponse(string Name, string Type, JsonElement Value, decimal? Minimum, decimal? Maximum);
