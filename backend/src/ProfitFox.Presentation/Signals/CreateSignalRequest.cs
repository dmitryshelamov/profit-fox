using System.Text.Json;

using ProfitFox.Domain.Signals;

namespace ProfitFox.Presentation.Signals;

/// <summary>
///     A new signal: its name, an optional description, the graph and the positions of its nodes.
/// </summary>
public sealed record CreateSignalRequest(string? Name, string? Description, JsonElement? Graph, IReadOnlyDictionary<string, NodePosition>? Layout);
