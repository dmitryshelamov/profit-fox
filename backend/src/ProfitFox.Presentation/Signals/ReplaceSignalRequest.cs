using System.Text.Json;
using ProfitFox.Domain.Signals;

namespace ProfitFox.Presentation.Signals;

/// <summary>
///     What replaces a saved signal: its description, graph and positions; the name stays.
/// </summary>
public sealed record ReplaceSignalRequest(string? Description, JsonElement? Graph, IReadOnlyDictionary<string, NodePosition>? Layout);
